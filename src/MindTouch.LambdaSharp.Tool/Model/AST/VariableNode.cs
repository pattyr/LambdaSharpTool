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

namespace MindTouch.LambdaSharp.Tool.Model.AST {

    public class VariableNode {

        //--- Constructors ---
        public VariableNode() { }

        public VariableNode(string value) {
            Value = value ?? throw new ArgumentNullException();
        }

        //--- Properties ---
        public string Prompt { get; set; }
        public string Encrypt { get; set; }
        public IList<VariableChoiceNode> Choices { get; set; }
        public string Value { get; set; }
    }

    public class VariableChoiceNode {

        //--- Properties ---
        public string Description { get; set; }
        public string Value { get; set; }
    }
}