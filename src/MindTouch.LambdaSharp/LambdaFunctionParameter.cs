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

using System.Collections.Generic;
using Newtonsoft.Json;
using Newtonsoft.Json.Converters;

namespace MindTouch.LambdaSharp {

    [JsonConverter(typeof(StringEnumConverter))]
    public enum LambdaFunctionParameterType {
        Text,
        Secret,
        Collection,
        Stack
    }

    public class LambdaFunctionParameter {

        //--- Properties ---
        public LambdaFunctionParameterType Type { get; set; }
        public object Value { get; set; }
        public Dictionary<string, string> EncryptionContext { get; set; }
    }
}