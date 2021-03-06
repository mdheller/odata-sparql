//   Copyright 2011 Microsoft Corporation
//
//   Licensed under the Apache License, Version 2.0 (the "License");
//   you may not use this file except in compliance with the License.
//   You may obtain a copy of the License at
//
//       http://www.apache.org/licenses/LICENSE-2.0
//
//   Unless required by applicable law or agreed to in writing, software
//   distributed under the License is distributed on an "AS IS" BASIS,
//   WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied.
//   See the License for the specific language governing permissions and
//   limitations under the License.

using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.Data.Edm.Csdl.Internal.Parsing.Ast;
using Microsoft.Data.Edm.Expressions;
using Microsoft.Data.Edm.Internal;
using Microsoft.Data.Edm.Validation;

namespace Microsoft.Data.Edm.Csdl.Internal.CsdlSemantics
{
    internal class CsdlSemanticsParameterReferenceExpression : CsdlSemanticsExpression, IEdmParameterReferenceExpression, IEdmCheckable
    {
        private readonly CsdlParameterReferenceExpression expression;
        private readonly IEdmEntityType bindingContext;

        private readonly Cache<CsdlSemanticsParameterReferenceExpression, IEdmFunctionParameter> referencedCache = new Cache<CsdlSemanticsParameterReferenceExpression, IEdmFunctionParameter>();
        private static readonly Func<CsdlSemanticsParameterReferenceExpression, IEdmFunctionParameter> ComputeReferencedFunc = (me) => me.ComputeReferenced();

        public CsdlSemanticsParameterReferenceExpression(CsdlParameterReferenceExpression expression, IEdmEntityType bindingContext, CsdlSemanticsSchema schema)
            : base(schema, expression)
        {
            this.expression = expression;
            this.bindingContext = bindingContext;
        }

        public override CsdlElement Element
        {
            get { return this.expression; }
        }

        public override EdmExpressionKind ExpressionKind
        {
            get { return EdmExpressionKind.ParameterReference; }
        }

        public IEdmFunctionParameter ReferencedParameter
        {
            get { return this.referencedCache.GetValue(this, ComputeReferencedFunc, null); }
        }

        public IEnumerable<EdmError> Errors
        {
            get
            {
                if (this.ReferencedParameter is IUnresolvedElement)
                {
                    return this.ReferencedParameter.Errors();
                }

                return Enumerable.Empty<EdmError>();
            }
        }

        private IEdmFunctionParameter ComputeReferenced()
        {
            return new UnresolvedParameter(new UnresolvedFunction(String.Empty, Edm.Strings.Bad_UnresolvedFunction(String.Empty), this.Location), this.expression.Parameter, this.Location);
        }
    }
}
