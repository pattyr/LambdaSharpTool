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
using System.Threading.Tasks;
using Amazon.SimpleSystemsManagement;
using McMaster.Extensions.CommandLineUtils;
using MindTouch.LambdaSharp.Tool.Internal;

namespace MindTouch.LambdaSharp.Tool.Cli {

    public class CliInfoCommand : ACliCommand {

        //--- Methods ---
        public void Register(CommandLineApplication app) {
            app.Command("info",  cmd => {
                cmd.HelpOption();
                cmd.Description = "Show LambdaSharp settings";

                // command options
                var initSettingsCallback = CreateSettingsInitializer(cmd);
                cmd.OnExecute(async () => {
                    Console.WriteLine($"{app.FullName} - {cmd.Description}");
                    var settings = await initSettingsCallback();
                    if(settings == null) {
                        return;
                    }
                    await Info(settings);
                });
            });
        }

        private async Task Info(Settings settings) {
            var ssmClient = new AmazonSimpleSystemsManagementClient();

            // import LambdaSharp settings
            var lambdaSharpPath = $"/{settings.Tier}/LambdaSharp/";
            var lambdaSharpSettings = await ssmClient.GetAllParametersByPathAsync(lambdaSharpPath);
            if(settings.EnvironmentVersion == null) {
                var version = GetLambdaSharpSetting("Version");
                if(version != null) {
                    settings.EnvironmentVersion = new Version(version);
                }
            }
            settings.DeploymentBucketName = settings.DeploymentBucketName ?? GetLambdaSharpSetting("DeploymentBucket");
            settings.DeadLetterQueueUrl = settings.DeadLetterQueueUrl ?? GetLambdaSharpSetting("DeadLetterQueue");
            settings.LoggingTopicArn = settings.LoggingTopicArn ?? GetLambdaSharpSetting("LoggingTopic");
            settings.NotificationTopicArn = settings.NotificationTopicArn ?? GetLambdaSharpSetting("DeploymentNotificationTopic");
            settings.RollbarCustomResourceTopicArn = settings.RollbarCustomResourceTopicArn ?? GetLambdaSharpSetting("RollbarCustomResourceTopic");
            settings.S3PackageLoaderCustomResourceTopicArn = settings.S3PackageLoaderCustomResourceTopicArn ?? GetLambdaSharpSetting("S3PackageLoaderCustomResourceTopic");

            // show LambdaSharp settings
            Console.WriteLine($"Deployment tier: {settings.Tier ?? "<NOT SET>"}");
            Console.WriteLine($"Git SHA: {settings.GitSha ?? "<NOT SET>"}");
            Console.WriteLine($"AWS Region: {settings.AwsRegion ?? "<NOT SET>"}");
            Console.WriteLine($"AWS Account Id: {settings.AwsAccountId ?? "<NOT SET>"}");
            Console.WriteLine($"LambdaSharp Environment Version: {settings.EnvironmentVersion?.ToString() ?? "<NOT SET>"}");
            Console.WriteLine($"LambdaSharp S3 Bucket: {settings.DeploymentBucketName ?? "<NOT SET>"}");
            Console.WriteLine($"LambdaSharp Dead-Letter Queue: {settings.DeadLetterQueueUrl ?? "<NOT SET>"}");
            Console.WriteLine($"LambdaSharp Logging Topic: {settings.LoggingTopicArn ?? "<NOT SET>"}");
            Console.WriteLine($"LambdaSharp CloudFormation Notification Topic: {settings.NotificationTopicArn ?? "<NOT SET>"}");
            Console.WriteLine($"LambdaSharp Rollbar Project Topic: {settings.RollbarCustomResourceTopicArn ?? "<NOT SET>"}");
            Console.WriteLine($"LambdaSharp S3 Package Loader Topic: {settings.S3PackageLoaderCustomResourceTopicArn ?? "<NOT SET>"}");

            // local functions
            string GetLambdaSharpSetting(string name) {
                lambdaSharpSettings.TryGetValue(lambdaSharpPath + name, out KeyValuePair<string, string> result);
                return result.Value;
            }
        }
    }
}
