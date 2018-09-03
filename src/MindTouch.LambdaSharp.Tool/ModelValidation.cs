/*
 * MindTouch λ#
 * Copyright (C) 2018 MindTouch, Inc.
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
using System.Text.RegularExpressions;
using MindTouch.LambdaSharp.Tool.Model.AST;

namespace MindTouch.LambdaSharp.Tool {

    public class ModelValidation : AModelProcessor {

        //--- Constants ---
        private const string SECRET_ALIAS_PATTERN = "[0-9a-zA-Z/_\\-]+";

        //--- Fields ---
        private ModuleNode _module;

        //--- Constructors ---
        public ModelValidation(Settings settings) : base(settings) { }

        //--- Methods ---
        public void Process(ModuleNode module) {
            Validate(module);
        }

        private void Validate(ModuleNode module) {
            _module = module;
            Validate(module.Name != null, "missing module name");

            // ensure collections are present
            if(module.Secrets == null) {
                module.Secrets = new List<string>();
            }
            if(module.Parameters == null) {
                module.Parameters = new List<ParameterNode>();
            }
            if(module.Functions == null) {
                module.Functions = new List<FunctionNode>();
            }

            // ensure version is present
            if(module.Version == null) {
                module.Version = "1.0";
            } else if(!Version.TryParse(module.Version, out System.Version version)) {
                AddError("`Version` expected to have format: Major.Minor[.Build[.Revision]]");
                module.Version = "0.0";
            }

            // process data structures
            AtLocation("Secrets", () => ValidateSecrets(module.Secrets));
            AtLocation("Parameters", () => ValidateParameters(module.Parameters));
            AtLocation("Functions", () => ValidateFunctions(module.Functions));
        }

        private void ValidateSecrets(IEnumerable<string> secrets) {
            var index = 0;
            foreach(var secret in secrets) {
                ++index;
                AtLocation($"[{index}]", () => {
                    if(string.IsNullOrEmpty(secret)) {
                        AddError($"secret has no value");
                    } else if(secret.Equals("aws/ssm", StringComparison.OrdinalIgnoreCase)) {
                        AddError($"cannot grant permission to decrypt with aws/ssm");
                    } else if(secret.StartsWith("arn:")) {
                        if(!Regex.IsMatch(secret, $"arn:aws:kms:{Settings.AwsRegion}:{Settings.AwsAccountId}:key/[a-fA-F0-9\\-]+")) {
                            AddError("secret key must be a valid ARN for the current region and account ID");
                        }
                    } else if(!Regex.IsMatch(secret, SECRET_ALIAS_PATTERN)) {
                        AddError("secret key must be a valid alias");
                    }
                });
            }
        }

        private void ValidateParameters(IEnumerable<ParameterNode> parameters, int depth = 0) {
            var index = 0;
            foreach(var parameter in parameters) {
                ++index;
                AtLocation(parameter.Name ?? $"[{index}]", () => {
                    Validate(parameter.Name != null, "missing parameter name");
                    Validate(Regex.IsMatch(parameter.Name, CLOUDFORMATION_ID_PATTERN), "parameter name is not valid");
                    if(parameter.Secret != null) {
                        ValidateNotBothStatements("Secret", "Resource", parameter.Resource == null);
                        ValidateNotBothStatements("Secret", "Values", parameter.Values == null);
                        ValidateNotBothStatements("Secret", "Value", parameter.Value == null);
                        ValidateNotBothStatements("Secret", "Package", parameter.Package == null);

                        // ensure parameter is not exported
                        if(parameter.Export != null) {
                            AddError("exporting Secret is not supported");
                        }
                    } else if(parameter.Values != null) {
                        ValidateNotBothStatements("Values", "Secret", parameter.Secret == null);
                        ValidateNotBothStatements("Values", "EncryptionContext", parameter.EncryptionContext == null);
                        ValidateNotBothStatements("Values", "Value", parameter.Value == null);
                        ValidateNotBothStatements("Values", "Package", parameter.Package == null);

                        // NOTE (2018-08-20, bjorg): special validation because of current `Values` expansion for resources
                        if((parameter.Resource != null) && (parameter.Parameters != null)) {
                            AddError("multiple values with a resource cannot have nested parameters");
                        }
                    } else if(parameter.Package != null) {
                        ValidateNotBothStatements("Package", "Resource", parameter.Resource == null);
                        ValidateNotBothStatements("Package", "Values", parameter.Values == null);
                        ValidateNotBothStatements("Package", "Value", parameter.Value == null);
                        ValidateNotBothStatements("Package", "Export", parameter.Export == null);
                        ValidateNotBothStatements("Values", "Secret", parameter.Secret == null);
                        ValidateNotBothStatements("Package", "EncryptionContext", parameter.EncryptionContext == null);

                        // ensure parameter is not exported
                        Validate(parameter.Export == null, "exporting Package is not supported");
                        Validate(parameter.Package.PackagePath == null, "'PackagePath' is reserved for internal use");

                        // check if required attributes are present
                        if(parameter.Package.S3Location == null) {
                            Validate(parameter.Package.Files != null, "missing 'Files' attribute");
                            Validate(parameter.Package.Bucket != null, "missing 'Bucket' attribute");
                        }
                        if(parameter.Package.Bucket != null) {

                            // verify that target bucket is defined as parameter with correct type
                            var param = _module.Parameters.FirstOrDefault(p => p.Name == parameter.Package.Bucket);
                            if(param == null) {
                                AddError($"could not find parameter for S3 bucket: '{parameter.Package.Bucket}'");
                            } else if(param?.Resource?.Type != "AWS::S3::Bucket") {
                                AddError($"parameter for function source must be an S3 bucket resource: '{parameter.Package.Bucket}'");
                            }
                        }
                        if(parameter.Package.Files != null) {

                            // check if a deployment bucket exists
                            if(Settings.DeploymentBucketName == null) {
                                AddError("deploying packages requires a deployment bucket", new LambdaSharpDeploymentTierSetupException(Settings.Tier));
                            }

                            // check if S3 package loader topic arn exists
                            if(Settings.S3PackageLoaderCustomResourceTopicArn == null) {
                                AddError("parameter package requires S3PackageLoader custom resource handler to be deployed", new LambdaSharpDeploymentTierSetupException(Settings.Tier));
                            }
                        }

                        // check if package is nested
                        if(depth > 0) {
                            AddError("parameter package cannot be nested");
                        }
                    }
                    if(parameter.Parameters != null) {
                        AtLocation("Parameters", () => {

                            // ensure parameter is not exported
                            Validate(parameter.Export == null, "exporting Parameters is not supported");

                            // recursively validate nested parameters
                            ValidateParameters(parameter.Parameters, depth + 1);
                        });
                    }
                    if(parameter.Resource != null) {
                        AtLocation("Resource", () => ValidateResource(parameter, parameter.Resource));
                    }
                });
            }
        }

        private void ValidateResource(ParameterNode parameter, ResourceNode resource) {
            if(parameter.Value != null) {
                resource.Type = resource.Type ?? "AWS";
                ValidateNotBothStatements("Value", "Properties", resource.Properties == null);
                ValidateARN(parameter.Value);
            } else if(parameter.Values != null) {
                resource.Type = resource.Type ?? "AWS";
                ValidateNotBothStatements("Values", "Properties", resource.Properties == null);
                foreach(var value in parameter.Values) {
                    ValidateARN(value);
                }
            } else if(resource.Type == null) {
                AddError("missing Type field");
            } else if(
                resource.Type.StartsWith("AWS::")
                && !Settings.ResourceMapping.IsResourceTypeSupported(resource.Type)
            ) {
                AddError($"unsupported resource type: {resource.Type}");
            }

            // local functions
            void ValidateARN(string resourceArn) {
                if(!resourceArn.StartsWith("arn:") && (resourceArn != "*")) {
                    AddError($"resource name must be a valid ARN or wildcard: {resourceArn}");
                }
            }
        }

        private void ValidateFunctions(IEnumerable<FunctionNode> functions) {

            // check if a dead-letter queue was specified
            if(Settings.DeadLetterQueueUrl == null) {
                AddError("deploying functions requires a dead-letter queue", new LambdaSharpDeploymentTierSetupException(Settings.Tier));
            }

            // check if a logging topic was set
            if(Settings.LoggingTopicArn == null) {
                AddError("deploying functions requires a logging topic", new LambdaSharpDeploymentTierSetupException(Settings.Tier));
            }

            // validate functions
            var index = 0;
            foreach(var function in functions) {
                ++index;
                AtLocation(function.Name ?? $"[{index}]", () => {
                    Validate(function.Name != null, "missing function name");
                    Validate(function.Memory != null, "missing Memory field");
                    Validate(int.TryParse(function.Memory, out _), "invalid Memory value");
                    Validate(function.Timeout != null, "missing Name field");
                    Validate(int.TryParse(function.Timeout, out _), "invalid Timeout value");
                    Validate(function.PackagePath == null, "'PackagePath' is reserved for internal use");
                });
            }
        }

        private void ValidateFunctionSource(IEnumerable<FunctionSourceNode> sources) {
            var index = 0;
            foreach(var source in sources) {
                ++index;
                AtLocation($"{index}", () => {
                    if(source.Api != null) {
                        ValidateNotBothStatements("Api", "Schedule", source.S3 == null);
                        ValidateNotBothStatements("Api", "Name", source.S3 == null);
                        ValidateNotBothStatements("Api", "S3", source.S3 == null);
                        ValidateNotBothStatements("Api", "Events", source.Events == null);
                        ValidateNotBothStatements("Api", "Prefix", source.Prefix == null);
                        ValidateNotBothStatements("Api", "Suffix", source.Suffix == null);
                        ValidateNotBothStatements("Api", "SlackCommand", source.SlackCommand == null);
                        ValidateNotBothStatements("Api", "Topic", source.Topic == null);
                        ValidateNotBothStatements("Api", "Sqs", source.Sqs == null);
                        ValidateNotBothStatements("Api", "BatchSize", source.BatchSize == null);
                        ValidateNotBothStatements("Api", "Alexa", source.Alexa == null);
                    } else if(source.Schedule != null) {
                        ValidateNotBothStatements("Schedule", "Api", source.Api == null);
                        ValidateNotBothStatements("Schedule", "Integration", source.Integration == null);
                        ValidateNotBothStatements("Schedule", "S3", source.S3 == null);
                        ValidateNotBothStatements("Schedule", "Events", source.Events == null);
                        ValidateNotBothStatements("Schedule", "Prefix", source.Prefix == null);
                        ValidateNotBothStatements("Schedule", "Suffix", source.Suffix == null);
                        ValidateNotBothStatements("Schedule", "SlackCommand", source.SlackCommand == null);
                        ValidateNotBothStatements("Schedule", "Topic", source.Topic == null);
                        ValidateNotBothStatements("Schedule", "Sqs", source.Sqs == null);
                        ValidateNotBothStatements("Schedule", "BatchSize", source.BatchSize == null);
                        ValidateNotBothStatements("Schedule", "Alexa", source.Alexa == null);

                        // TODO (2018-06-27, bjorg): add cron/rate expression validation
                    } else if(source.S3 != null) {
                        ValidateNotBothStatements("S3", "Api", source.Api == null);
                        ValidateNotBothStatements("S3", "Integration", source.Integration == null);
                        ValidateNotBothStatements("S3", "Schedule", source.S3 == null);
                        ValidateNotBothStatements("S3", "Name", source.S3 == null);
                        ValidateNotBothStatements("S3", "SlackCommand", source.SlackCommand == null);
                        ValidateNotBothStatements("S3", "Topic", source.Topic == null);
                        ValidateNotBothStatements("S3", "Sqs", source.Sqs == null);
                        ValidateNotBothStatements("S3", "BatchSize", source.BatchSize == null);
                        ValidateNotBothStatements("S3", "Alexa", source.Alexa == null);

                        // check if S3 subscriber topic arn exists
                        if(Settings.S3SubscriberCustomResourceTopicArn == null) {
                            AddError("S3 source requires S3Subscriber custom resource handler to be deployed", new LambdaSharpDeploymentTierSetupException(Settings.Tier));
                        }

                        // TODO (2018-06-27, bjorg): add events, prefix, suffix validation

                        // verify source exists
                        ValidateSourceParameter(source.S3, "AWS::S3::Bucket", "S3 bucket");
                    } else if(source.SlackCommand != null) {
                        ValidateNotBothStatements("SlackCommand", "Api", source.Api == null);
                        ValidateNotBothStatements("SlackCommand", "Integration", source.Integration == null);
                        ValidateNotBothStatements("SlackCommand", "Schedule", source.S3 == null);
                        ValidateNotBothStatements("SlackCommand", "Name", source.S3 == null);
                        ValidateNotBothStatements("SlackCommand", "S3", source.S3 == null);
                        ValidateNotBothStatements("SlackCommand", "Events", source.Events == null);
                        ValidateNotBothStatements("SlackCommand", "Prefix", source.Prefix == null);
                        ValidateNotBothStatements("SlackCommand", "Suffix", source.Suffix == null);
                        ValidateNotBothStatements("SlackCommand", "Topic", source.Topic == null);
                        ValidateNotBothStatements("SlackCommand", "Sqs", source.Sqs == null);
                        ValidateNotBothStatements("SlackCommand", "BatchSize", source.BatchSize == null);
                        ValidateNotBothStatements("SlackCommand", "Alexa", source.Alexa == null);
                    } else if(source.Topic != null) {
                        ValidateNotBothStatements("Topic", "Api", source.Api == null);
                        ValidateNotBothStatements("Topic", "Integration", source.Integration == null);
                        ValidateNotBothStatements("Topic", "Schedule", source.S3 == null);
                        ValidateNotBothStatements("Topic", "Name", source.S3 == null);
                        ValidateNotBothStatements("Topic", "S3", source.S3 == null);
                        ValidateNotBothStatements("Topic", "Events", source.Events == null);
                        ValidateNotBothStatements("Topic", "Prefix", source.Prefix == null);
                        ValidateNotBothStatements("Topic", "Suffix", source.Suffix == null);
                        ValidateNotBothStatements("Topic", "SlackCommand", source.SlackCommand == null);
                        ValidateNotBothStatements("Topic", "Sqs", source.Sqs == null);
                        ValidateNotBothStatements("Topic", "BatchSize", source.BatchSize == null);
                        ValidateNotBothStatements("Topic", "Alexa", source.Alexa == null);

                        // verify source exists
                        ValidateSourceParameter(source.Topic, "AWS::SNS::Topic", "SNS topic");
                    } else if(source.Sqs != null) {
                        ValidateNotBothStatements("Sqs", "Api", source.Api == null);
                        ValidateNotBothStatements("Sqs", "Integration", source.Integration == null);
                        ValidateNotBothStatements("Sqs", "Schedule", source.S3 == null);
                        ValidateNotBothStatements("Sqs", "Name", source.S3 == null);
                        ValidateNotBothStatements("Sqs", "S3", source.S3 == null);
                        ValidateNotBothStatements("Sqs", "Events", source.Events == null);
                        ValidateNotBothStatements("Sqs", "Prefix", source.Prefix == null);
                        ValidateNotBothStatements("Sqs", "Suffix", source.Suffix == null);
                        ValidateNotBothStatements("Sqs", "SlackCommand", source.SlackCommand == null);
                        ValidateNotBothStatements("Sqs", "Topic", source.Topic == null);
                        ValidateNotBothStatements("Sqs", "Alexa", source.Alexa == null);

                        // validate settings
                        AtLocation("BatchSize", () => {
                            if((source.BatchSize < 1) || (source.BatchSize > 10)) {
                                AddError($"invalid BatchSize value: {source.BatchSize}");
                            }
                        });

                        // verify source exists
                        ValidateSourceParameter(source.Sqs, "AWS::SQS::Queue", "SQS queue");
                    } else if(source.Alexa != null) {
                        ValidateNotBothStatements("Alexa", "Api", source.Api == null);
                        ValidateNotBothStatements("Alexa", "Integration", source.Integration == null);
                        ValidateNotBothStatements("Alexa", "Schedule", source.S3 == null);
                        ValidateNotBothStatements("Alexa", "Name", source.S3 == null);
                        ValidateNotBothStatements("Alexa", "S3", source.S3 == null);
                        ValidateNotBothStatements("Alexa", "Events", source.Events == null);
                        ValidateNotBothStatements("Alexa", "Prefix", source.Prefix == null);
                        ValidateNotBothStatements("Alexa", "Suffix", source.Suffix == null);
                        ValidateNotBothStatements("Alexa", "SlackCommand", source.SlackCommand == null);
                        ValidateNotBothStatements("Alexa", "Topic", source.Topic == null);
                        ValidateNotBothStatements("Alexa", "Sqs", source.Sqs == null);
                        ValidateNotBothStatements("Alexa", "BatchSize", source.BatchSize == null);
                    } else {
                        AddError("unknown source");
                    }
                });
            }
        }

        // local functions
        private void ValidateNotBothStatements(string attribute1, string attribute2, bool condition) {
            if(!condition) {
                AddError($"attributes '{attribute1}' and '{attribute2}' are not allowed at the same time");
            }
        }

        private void ValidateSourceParameter(string name, string awsType, string typeDescription) {
            var parameter = _module.Parameters.FirstOrDefault(p => p.Name == name);
            if(parameter == null) {
                AddError($"could not find parameter for {typeDescription}: '{name}'");
            } else if(parameter?.Resource?.Type != awsType) {
                AddError($"parameter for function source must be an {typeDescription} resource: '{name}'");
            }
        }
    }
}