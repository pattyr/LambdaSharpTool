/*
 * MindTouch λ#
 * Copyright (C) 2006-2018 MindTouch, Inc.
 * www.mindtouch.com  oss@mindtouch.com
 *
 * For community documentation and downloads visit mindtouch.com;
 * please review the licensing section.
 *
 * Licensed under the Apache License, Version 2.0 (the "License");
 * you may not use this file except in compliance with the License.
 * You may obtain a copy of the License at
 *
 *     http://www.apache.org/licenses/LICENSE-2.0
 *
 * Unless required by applicable law or agreed to in writing, software
 * distributed under the License is distributed on an "AS IS" BASIS,
 * WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied.
 * See the License for the specific language governing permissions and
 * limitations under the License.
 */

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading.Tasks;
using Amazon.CloudFormation;
using Amazon.KeyManagementService;
using Amazon.S3;
using Amazon.SecurityToken;
using Amazon.SecurityToken.Model;
using Amazon.SimpleSystemsManagement;
using McMaster.Extensions.CommandLineUtils;
using MindTouch.LambdaSharp.Tool.Internal;
using MindTouch.LambdaSharp.Tool.Model;

namespace MindTouch.LambdaSharp.Tool.Cli {

    public abstract class ACliCommand : CliBase {

        //--- Methods ---
        protected Func<Task<Settings>> CreateSettingsInitializer(CommandLineApplication cmd) {
            var deploymentOption = cmd.Option("--deployment|-D <NAME>", "(optional) Name of deployment (default: LAMBDASHARPDEPLOYMENT environment variable)", CommandOptionType.SingleValue);
            var awsProfileOption = cmd.Option("--profile|-P <NAME>", "(optional) Use a specific AWS profile from the AWS credentials file", CommandOptionType.SingleValue);
            var verboseLevelOption = cmd.Option("--verbose|-V:<LEVEL>", "(optional) Show verbose output (0=quiet, 1=normal, 2=detailed, 3=exceptions)", CommandOptionType.SingleOrNoValue);
            var gitShaOption = cmd.Option("--gitsha <VALUE>", "(optional) GitSha of most recent git commit (default: invoke `git rev-parse HEAD` command)", CommandOptionType.SingleValue);
            var awsAccountIdOption = cmd.Option("--aws-account-id <VALUE>", "(test only) Override AWS account Id (default: read from AWS profile)", CommandOptionType.SingleValue);
            var awsRegionOption = cmd.Option("--aws-region <NAME>", "(test only) Override AWS region (default: read from AWS profile)", CommandOptionType.SingleValue);
            var deploymentBucketNameOption = cmd.Option("--deployment-bucket-name <NAME>", "(test only) S3 Bucket used to deploying assets (default: read from LambdaSharp configuration)", CommandOptionType.SingleValue);
            var deploymentDeadletterQueueUrlOption = cmd.Option("--deployment-deadletter-queue-url <URL>", "(test only) SQS Deadletter queue used by function (default: read from LambdaSharp configuration)", CommandOptionType.SingleValue);
            var deploymentNotificationTopicArnOption = cmd.Option("--deployment-notification-topic-arn <ARN>", "(test only) SNS Topic used by CloudFormation deploymetions (default: read from LambdaSharp configuration)", CommandOptionType.SingleValue);
            var boostrapOption = cmd.Option("--bootstrap", "(boostrap only) Don't read LambdaSharp initialization values", CommandOptionType.NoValue);
            var deploymentRollbarCustomResourceTopicArnOption = cmd.Option("--deployment-rollbar-customresource-topic-arn <ARN>", "(test only) SNS Topic for creating Rollbar projects (default: read from LambdaSharp configuration)", CommandOptionType.SingleValue);
            var deploymentS3PackageLoaderCustomResourceTopicArnOption = cmd.Option("--deployment-s3packageloader-customresource-topic-arn <ARN>", "(test only) SNS Topic for synchronizing S3 buckets (default: read from LambdaSharp configuration)", CommandOptionType.SingleValue);
            return async () => {
                var boostrap = boostrapOption.HasValue();

                // initialize logging level
                if(verboseLevelOption.HasValue()) {
                    if(!TryParseEnumOption(verboseLevelOption, VerboseLevel.Detailed, out _verboseLevel)) {
                        return null;
                    }
                }

                // initialize deployment value
                var deployment = deploymentOption.Value() ?? Environment.GetEnvironmentVariable("LAMBDASHARPDEPLOYMENT");
                if(deployment == null) {
                    AddError("missing 'deployment' name");
                    return null;
                }
                if(deployment == "Default") {
                    AddError("deployment cannot be 'Default' because it is a reserved name");
                    return null;
                }

                // initialize gitSha value
                var gitSha = gitShaOption.Value();
                if(gitSha == null) {

                    // read the gitSha using `git` directly
                    var process = new Process() {
                        StartInfo = new ProcessStartInfo("git", ArgumentEscaper.EscapeAndConcatenate(new[] { "rev-parse", "HEAD" })) {
                            RedirectStandardOutput = true,
                            UseShellExecute = false
                        }
                    };
                    process.Start();
                    gitSha = process.StandardOutput.ReadToEnd().Trim();
                    process.WaitForExit();
                    if(process.ExitCode != 0) {
                        Console.WriteLine($"WARNING: unable to get git-sha `git rev-parse HEAD` failed with exit code = {process.ExitCode}");
                        gitSha = null;
                    }
                }

                // initialize AWS account Id and region
                var awsProfile = awsProfileOption.Value();
                if(awsProfile != null) {

                    // select an alternate AWS profile by setting the AWS_PROFILE environment variable
                    Environment.SetEnvironmentVariable("AWS_PROFILE", awsProfile);
                }
                var awsAccountId = awsAccountIdOption.Value();
                var awsRegion = awsRegionOption.Value();
                if((awsAccountId == null) || (awsRegion == null)) {

                    // determine AWS region and account
                    try {
                        var stsClient = new AmazonSecurityTokenServiceClient();
                        var response = await stsClient.GetCallerIdentityAsync(new GetCallerIdentityRequest());
                        awsAccountId = awsAccountId ?? response.Account;
                        awsRegion = awsRegion ?? stsClient.Config.RegionEndpoint.SystemName;
                    } catch(Exception e) {
                        AddError("unable to determine the AWS Account Id and Region", e);
                        return null;
                    }
                }

                // create AWS clients
                var ssmClient = new AmazonSimpleSystemsManagementClient();
                var cfClient = new AmazonCloudFormationClient();
                var kmsClient = new AmazonKeyManagementServiceClient();
                var s3Client = new AmazonS3Client();

                // initialize LambdaSharp deployment values
                var deploymentBucketName = deploymentBucketNameOption.Value();
                var deploymentDeadletterQueueUrl = deploymentDeadletterQueueUrlOption.Value();
                var deploymentNotificationTopicArn = deploymentNotificationTopicArnOption.Value();
                var deploymentRollbarCustomResourceTopicArn = deploymentRollbarCustomResourceTopicArnOption.Value();
                var deploymentS3PackageLoaderCustomResourceTopicArn = deploymentS3PackageLoaderCustomResourceTopicArnOption.Value();
                if(boostrap) {
                    Console.WriteLine($"Bootstrapping LambdaSharp for `{deployment}'");
                } else if(
                    (deploymentBucketName == null)
                    || (deploymentDeadletterQueueUrl == null)
                    || (deploymentNotificationTopicArn == null)
                    || (deploymentRollbarCustomResourceTopicArn == null)
                    || (deploymentS3PackageLoaderCustomResourceTopicArn == null)
                ) {
                    Console.WriteLine($"Retrieving LambdaSharp settings for `{deployment}'");

                    // import lambdasharp parameters
                    var lambdaSharpPath = $"/{deployment}/LambdaSharp/";
                    var lambdaSharpSettings = await ssmClient.GetAllParametersByPathAsync(lambdaSharpPath);
                    deploymentBucketName = deploymentBucketName ?? GetLambdaSharpSetting("DeploymentBucket");
                    if(deploymentBucketName == null) {
                        AddError("unable to determine the LambdaSharp S3 Bucket");
                        return null;
                    }
                    deploymentDeadletterQueueUrl = deploymentDeadletterQueueUrl ?? GetLambdaSharpSetting("DeadLetterQueue");
                    if(deploymentDeadletterQueueUrl == null) {
                        AddError("unable to determine the LambdaSharp Dead-Letter Queue");
                        return null;
                    }
                    deploymentNotificationTopicArn = deploymentNotificationTopicArn ?? GetLambdaSharpSetting("DeploymentNotificationTopic");
                    if(deploymentNotificationTopicArn == null) {
                        AddError("unable to determine the LambdaSharp CloudFormation Notification Topic");
                        return null;
                    }

                    // Rollbar custom topic is optional, so don't check for null
                    deploymentRollbarCustomResourceTopicArn = deploymentRollbarCustomResourceTopicArn ?? GetLambdaSharpSetting("RollbarCustomResourceTopic");

                    // S3 synchronization topic is optional, so don't check for null
                    deploymentS3PackageLoaderCustomResourceTopicArn = deploymentS3PackageLoaderCustomResourceTopicArn ?? GetLambdaSharpSetting("S3PackageLoaderCustomResourceTopic");

                    // local functions
                    string GetLambdaSharpSetting(string name) {
                        lambdaSharpSettings.TryGetValue(lambdaSharpPath + name, out KeyValuePair<string, string> result);
                        return result.Value;
                    }
                }
                return new Settings {
                    Deployment = deployment,
                    GitSha = gitSha,
                    AwsRegion = awsRegion,
                    AwsAccountId = awsAccountId,
                    DeploymentBucketName = deploymentBucketName,
                    DeadLetterQueueUrl = deploymentDeadletterQueueUrl,
                    DeploymentNotificationTopicArn = deploymentNotificationTopicArn,
                    RollbarCustomResourceTopicArn = deploymentRollbarCustomResourceTopicArn,
                    S3PackageLoaderCustomResourceTopicArn = deploymentS3PackageLoaderCustomResourceTopicArn,
                    ResourceMapping = new ResourceMapping(),
                    SsmClient = ssmClient,
                    CfClient = cfClient,
                    KmsClient = kmsClient,
                    S3Client = s3Client,
                    ErrorCallback = AddError,
                    VerboseLevel = _verboseLevel
                };
            };
        }

        protected static bool TryParseEnumOption<T>(CommandOption option, T defaultvalue, out T result) where T : struct {
            if(option.Value() == null) {
                result = defaultvalue;
                return true;
            }
            if(int.TryParse(option.Value(), out int intValue)) {
                if(!Enum.GetValues(typeof(T)).Cast<int>().Any(v => v == intValue)) {
                    goto failed;
                }
                result = (T)Convert.ChangeType(Enum.ToObject(typeof(T), intValue), typeof(T));
                return true;
            }
            if(Enum.TryParse(typeof(T), option.Value(), ignoreCase: true, result: out object enumValue)) {
                result = (T)Convert.ChangeType(enumValue, typeof(T));
                return true;
            }
        failed:
            var pairs = Enum.GetValues(typeof(T)).Cast<int>().Zip(Enum.GetNames(typeof(T)).Cast<string>(), (value, name) => $"{value}={name.ToLowerInvariant()}");
            AddError($"value for {option.Template} must be one of {string.Join(", ", pairs)}");
            result = defaultvalue;
            return false;
        }
    }
}