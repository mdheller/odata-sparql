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
using Microsoft.Data.Edm.Csdl.Internal.Parsing.Ast;
using Microsoft.Data.Edm.Expressions;
using Microsoft.Data.Edm.Internal;

namespace Microsoft.Data.Edm.Csdl.Internal.CsdlSemantics
{
    /// <summary>
    /// Provides semantics for a CsdlPropertyValue used in a record expression.
    /// </summary>
    internal class CsdlSemanticsPropertyConstructor : CsdlSemanticsElement, IEdmPropertyConstructor
    {
        private readonly CsdlPropertyValue property;
        private readonly CsdlSemanticsRecordExpression context;

        private readonly Cache<CsdlSemanticsPropertyConstructor, IEdmExpression> valueCache = new Cache<CsdlSemanticsPropertyConstructor, IEdmExpression>();
        private static readonly Func<CsdlSemanticsPropertyConstructor, IEdmExpression> ComputeValueFunc = (me) => me.ComputeValue();

        public CsdlSemanticsPropertyConstructor(CsdlPropertyValue property, CsdlSemanticsRecordExpression context)
            : base(property)
        {
            this.property = property;
            this.context = context;
        }

        public string Name
        {
            get { return this.property.Property; }
        }

        public IEdmExpression Value
        {
            get { return this.valueCache.GetValue(this, ComputeValueFunc, null); }
        }

        public override CsdlElement Element
        {
            get { return this.property; }
        }

        public override CsdlSemanticsModel Model
        {
            get { return this.context.Model; }
        }

        private IEdmExpression ComputeValue()
        {
            return CsdlSemanticsModel.WrapExpression(this.property.Expression, this.context.BindingContext, this.context.Schema);
        }
    }
}
