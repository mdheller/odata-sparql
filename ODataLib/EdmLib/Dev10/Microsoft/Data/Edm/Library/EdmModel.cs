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
using Microsoft.Data.Edm.Annotations;
using Microsoft.Data.Edm.Internal;
using Microsoft.Data.Edm.Library.Annotations;

namespace Microsoft.Data.Edm.Library
{
    /// <summary>
    /// Represents an EDM model.
    /// </summary>
    public class EdmModel : EdmModelBase
    {
        private readonly List<IEdmSchemaElement> elements = new List<IEdmSchemaElement>();
        private readonly Dictionary<IEdmVocabularyAnnotatable, List<IEdmVocabularyAnnotation>> vocabularyAnnotationsDictionary = new Dictionary<IEdmVocabularyAnnotatable, List<IEdmVocabularyAnnotation>>();
        private readonly Dictionary<IEdmStructuredType, List<IEdmStructuredType>> derivedTypeMappings = new Dictionary<IEdmStructuredType, List<IEdmStructuredType>>();

        /// <summary>
        /// Initializes a new instance of the <see cref="EdmModel"/> class.
        /// </summary>
        public EdmModel()
            : base(Enumerable.Empty<IEdmModel>(), new EdmDirectValueAnnotationsManager())
        {
        }

        /// <summary>
        /// Gets the collection of schema elements that are contained in this model.
        /// </summary>
        public override IEnumerable<IEdmSchemaElement> SchemaElements
        {
            get { return this.elements; }
        }

        /// <summary>
        /// Gets the collection of vocabulary annotations that are contained in this model.
        /// </summary>
        public override IEnumerable<IEdmVocabularyAnnotation> VocabularyAnnotations
        {
            get { return this.vocabularyAnnotationsDictionary.SelectMany(kvp => kvp.Value); }
        }

        /// <summary>
        /// Adds a model reference to this model.
        /// </summary>
        /// <param name="model">The model to reference.</param>
        public new void AddReferencedModel(IEdmModel model)
        {
            base.AddReferencedModel(model);
        }

        /// <summary>
        /// Adds a schema element to this model.
        /// </summary>
        /// <param name="element">Element to be added.</param>
        public void AddElement(IEdmSchemaElement element)
        {
            EdmUtil.CheckArgumentNull(element, "element");
            this.elements.Add(element);
            IEdmStructuredType structuredType = element as IEdmStructuredType;
            if (structuredType != null && structuredType.BaseType != null)
            {
                List<IEdmStructuredType> derivedTypes;
                if (!this.derivedTypeMappings.TryGetValue(structuredType.BaseType, out derivedTypes))
                {
                    derivedTypes = new List<IEdmStructuredType>();
                    this.derivedTypeMappings[structuredType.BaseType] = derivedTypes;
                }

                derivedTypes.Add(structuredType);
            }

            this.RegisterElement(element);
        }

        /// <summary>
        /// Adds a collection of schema elements to this model.
        /// </summary>
        /// <param name="newElements">Elements to be added.</param>
        public void AddElements(IEnumerable<IEdmSchemaElement> newElements)
        {
            EdmUtil.CheckArgumentNull(newElements, "newElements");
            foreach (IEdmSchemaElement element in newElements)
            {
                this.AddElement(element);
            }
        }

        /// <summary>
        /// Adds a vocabulary annotation to this model.
        /// </summary>
        /// <param name="annotation">The annotation to be added.</param>
        public void AddVocabularyAnnotation(IEdmVocabularyAnnotation annotation)
        {
            EdmUtil.CheckArgumentNull(annotation, "annotation");
            if (annotation.Target == null)
            {
                throw new InvalidOperationException(Strings.Constructable_VocabularyAnnotationMustHaveTarget);
            }

            List<IEdmVocabularyAnnotation> elementAnnotations;
            if (!this.vocabularyAnnotationsDictionary.TryGetValue(annotation.Target, out elementAnnotations))
            {
                elementAnnotations = new List<IEdmVocabularyAnnotation>();
                this.vocabularyAnnotationsDictionary.Add(annotation.Target, elementAnnotations);
            }

            elementAnnotations.Add(annotation);
        }

        /// <summary>
        /// Searches for vocabulary annotations specified by this model.
        /// </summary>
        /// <param name="element">The annotated element.</param>
        /// <returns>The vocabulary annotations for the element.</returns>
        public override IEnumerable<IEdmVocabularyAnnotation> FindDeclaredVocabularyAnnotations(IEdmVocabularyAnnotatable element)
        {
            List<IEdmVocabularyAnnotation> elementAnnotations;
            return this.vocabularyAnnotationsDictionary.TryGetValue(element, out elementAnnotations) ? elementAnnotations : Enumerable.Empty<IEdmVocabularyAnnotation>();
        }

        /// <summary>
        /// Finds a list of types that derive directly from the supplied type.
        /// </summary>
        /// <param name="baseType">The base type that derived types are being searched for.</param>
        /// <returns>A list of types from this model that derive directly from the given type.</returns>
        public override IEnumerable<IEdmStructuredType> FindDirectlyDerivedTypes(IEdmStructuredType baseType)
        {
            List<IEdmStructuredType> types;
            if (this.derivedTypeMappings.TryGetValue(baseType, out types))
            {
                return types;
            }

            return Enumerable.Empty<IEdmStructuredType>();
        }
    }
}
