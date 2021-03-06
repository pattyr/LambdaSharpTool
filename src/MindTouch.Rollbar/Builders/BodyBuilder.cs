﻿/*
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
using MindTouch.Rollbar.Data;

namespace MindTouch.Rollbar.Builders {
    public class BodyBuilder : IBodyBuilder {

        //--- Fields ---
        private readonly ITraceBuilder _traceBuilder;
        private readonly ITraceChainBuilder _traceChainBuilder;

        // -- Constructor ---
        public BodyBuilder(ITraceBuilder traceBuilder, ITraceChainBuilder traceChainBuilder) {
            if(traceBuilder == null) {
                throw new ArgumentNullException("traceBuilder");
            }
            if(traceChainBuilder == null) {
                throw new ArgumentNullException("traceChainBuilder");
            }
            _traceBuilder = traceBuilder;
            _traceChainBuilder = traceChainBuilder;
        }

        //--- Methods --- 
        public Body CreateFromException(Exception exception, string description) {
            if(exception.InnerException == null) {
                var trace = _traceBuilder.CreateFromException(exception, description);
                return new Body(trace);
            }
            var traces = _traceChainBuilder.CreateFromException(exception, description);
            return new Body(traces);
        }

        public Body CreateFromMessage(string message) {
            var m = new Message(message);
            return new Body(m);
        }
    }
}
