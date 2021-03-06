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

namespace Microsoft.Data.Edm.Library
{
    /// <summary>
    /// Represents a reference to an EDM temporal (Time, DateTime, DateTimeOffset) type.
    /// </summary>
    public class EdmTemporalTypeReference : EdmPrimitiveTypeReference, IEdmTemporalTypeReference
    {
        private readonly int? precision;

        /// <summary>
        /// Initializes a new instance of the <see cref="EdmTemporalTypeReference"/> class.
        /// </summary>
        /// <param name="definition">The type this reference refers to.</param>
        /// <param name="isNullable">Denotes whether the type can be nullable.</param>
        public EdmTemporalTypeReference(IEdmPrimitiveType definition, bool isNullable)
            : this(definition, isNullable, null)
        {
        }

        /// <summary>
        /// Initializes a new instance of the <see cref="EdmTemporalTypeReference"/> class.
        /// </summary>
        /// <param name="definition">The type this reference refers to.</param>
        /// <param name="isNullable">Denotes whether the type can be nullable.</param>
        /// <param name="precision">Precision of values with this type.</param>
        public EdmTemporalTypeReference(IEdmPrimitiveType definition, bool isNullable, int? precision)
            : base(definition, isNullable)
        {
            this.precision = precision;
        }

        /// <summary>
        /// Gets the precision of this temporal type.
        /// </summary>
        public int? Precision
        {
            get { return this.precision; }
        }
    }
}
