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
using System.Linq;
using System.Threading.Tasks;
using Amazon.CloudFormation;
using Amazon.CloudFormation.Model;
using McMaster.Extensions.CommandLineUtils;

namespace MindTouch.LambdaSharp.Tool.Cli {

    public class CliListCommand : ACliCommand {

        //--- Methods ---
        public void Register(CommandLineApplication app) {
            app.Command("list", cmd => {
                cmd.HelpOption();
                cmd.Description = "List LambdaSharp deployments";

                // command options
                var deploymentOption = cmd.Option("--deployment|-D <NAME>", "(optional) Name of deployment (default: LAMBDASHARPDEPLOYMENT environment variable)", CommandOptionType.SingleValue);
                var awsProfileOption = cmd.Option("--profile|-P <NAME>", "(optional) Use a specific AWS profile from the AWS credentials file", CommandOptionType.SingleValue);
                cmd.OnExecute(async () => {
                    Console.WriteLine($"{app.FullName} - {cmd.Description}");

                    // initialize deployment value
                    var deployment = deploymentOption.Value() ?? Environment.GetEnvironmentVariable("LAMBDASHARPDEPLOYMENT");
                    if(deployment == null) {
                        AddError("missing 'deployment' name");
                        return;
                    }
                    if(deployment == "Default") {
                        AddError("deployment cannot be 'Default' because it is a reserved name");
                        return;
                    }

                    // initialize AWS account Id and region
                    var awsProfile = awsProfileOption.Value();
                    if(awsProfile != null) {

                        // select an alternate AWS profile by setting the AWS_PROFILE environment variable
                        Environment.SetEnvironmentVariable("AWS_PROFILE", awsProfile);
                    }
                    await List(deployment);
                });
            });
        }

        private async Task List(string deployment) {
            var cfClient = new AmazonCloudFormationClient();
            var request = new ListStacksRequest {
                StackStatusFilter = new List<string> {
                    "CREATE_IN_PROGRESS",
                    "CREATE_FAILED",
                    "CREATE_COMPLETE",
                    "ROLLBACK_IN_PROGRESS",
                    "ROLLBACK_FAILED",
                    "ROLLBACK_COMPLETE",
                    "DELETE_IN_PROGRESS",
                    "DELETE_FAILED",
                    // "DELETE_COMPLETE",
                    "UPDATE_IN_PROGRESS",
                    "UPDATE_COMPLETE_CLEANUP_IN_PROGRESS",
                    "UPDATE_COMPLETE",
                    "UPDATE_ROLLBACK_IN_PROGRESS",
                    "UPDATE_ROLLBACK_FAILED",
                    "UPDATE_ROLLBACK_COMPLETE_CLEANUP_IN_PROGRESS",
                    "UPDATE_ROLLBACK_COMPLETE",
                    "REVIEW_IN_PROGRESS"
                }
            };

            // fetch all stacks
            var prefix = $"{deployment}-";
            var summaries = new List<StackSummary>();
            do {
                var response = await cfClient.ListStacksAsync(request);
                summaries.AddRange(response.StackSummaries.Where(summary => summary.StackName.StartsWith(prefix, StringComparison.Ordinal)));
                request.NextToken = response.NextToken;
            } while(request.NextToken != null);

            // sort and format output
            if(summaries.Any()) {
                var nameWidth = summaries.Max(summary => summary.StackName.Length) + 4;
                var statusWidth = summaries.Max(summary => summary.StackStatus.ToString().Length) + 4;
                Console.WriteLine();
                Console.WriteLine($"{"STACK NAME".PadRight(nameWidth)}{"STATUS".PadRight(statusWidth)}DATE");
                foreach(var summary in summaries.Select(summary => new {
                    StackName = summary.StackName,
                    StackStatus = summary.StackStatus,
                    Date = (summary.LastUpdatedTime > summary.CreationTime) ? summary.LastUpdatedTime : summary.CreationTime
                }).OrderBy(summary => summary.Date)) {
                    Console.WriteLine($"{summary.StackName.PadRight(nameWidth)}{("[" + summary.StackStatus + "]").PadRight(statusWidth)}{summary.Date:yyyy-MM-dd HH:mm:ss}");
                }
            } else {
                Console.WriteLine();
                Console.WriteLine("No stacks found.");
            }
        }
    }
}