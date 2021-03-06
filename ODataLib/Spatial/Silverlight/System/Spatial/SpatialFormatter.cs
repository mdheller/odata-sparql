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

namespace System.Spatial
{
    using System.Collections.Generic;
    using System.Diagnostics.CodeAnalysis;

    /// <summary>
    /// Base class for all Spatial Formats
    /// </summary>
    /// <typeparam name="TReaderStream">The type of reader to be read from.</typeparam>
    /// <typeparam name="TWriterStream">The type of writer stream to write to.</typeparam>
    public abstract class SpatialFormatter<TReaderStream, TWriterStream>
    {
        /// <summary>
        /// The implementation that created this instance.
        /// </summary>
        private readonly SpatialImplementation creator;

        /// <summary>
        /// Initializes a new instance of the <see cref="SpatialFormatter{TReaderStream, TWriterStream}"/> class.
        /// </summary>
        /// <param name="creator">The implementation that created this instance.</param>
        protected SpatialFormatter(SpatialImplementation creator)
        {
            Util.CheckArgumentNull(creator, "creator");
            this.creator = creator;
        }

        /// <summary>
        /// Parses the input, and produces the object
        /// </summary>
        /// <typeparam name="TResult">The type of object to produce</typeparam>
        /// <param name="input">The input to be parsed.</param>
        /// <returns>An object of TResultType</returns>
        public TResult Read<TResult>(TReaderStream input) where TResult : class, ISpatial
        {
            var trans = MakeValidatingBuilder();
            IShapeProvider parsePipelineEnd = trans.Value;

            Read<TResult>(input, trans.Key);
            if (typeof(Geometry).IsAssignableFrom(typeof(TResult)))
            {
                return (TResult)(object)parsePipelineEnd.ConstructedGeometry;
            }
            else
            {
                return (TResult)(object)parsePipelineEnd.ConstructedGeography;
            }
        }

        /// <summary>
        /// Parses the input, and produces the object
        /// </summary>
        /// <typeparam name="TResult">The type of object to produce</typeparam>
        /// <param name="input">The input to be parsed.</param>
        /// <param name="pipeline">The pipeline to call during reading.</param>
        [SuppressMessage("Microsoft.Design", "CA1004:GenericMethodsShouldProvideTypeParameter", Justification = "The type hierarchy is too deep to have a specificly typed Read for each of them.")]
        public void Read<TResult>(TReaderStream input, SpatialPipeline pipeline) where TResult : class, ISpatial
        {
            if (typeof(Geometry).IsAssignableFrom(typeof(TResult)))
            {
                ReadGeometry(input, pipeline);
            }
            else
            {
                ReadGeography(input, pipeline);
            }
        }

        /// <summary>
        /// Creates a valid format from the spatial object.
        /// </summary>
        /// <param name="spatial">The object that the format is being created for.</param>
        /// <param name="writerStream">The stream to write the formatted object to.</param>
        public void Write(ISpatial spatial, TWriterStream writerStream)
        {
            var writer = this.CreateWriter(writerStream);
            spatial.SendTo(writer);
        }

        /// <summary>
        /// Creates the writerStream.
        /// </summary>
        /// <param name="writerStream">The stream that should be written to.</param>
        /// <returns>The writerStream that was created.</returns>
        public abstract SpatialPipeline CreateWriter(TWriterStream writerStream);

        /// <summary>
        /// Read the Geography from the readerStream and call the appopriate pipeline methods
        /// </summary>
        /// <param name="readerStream">The stream to read from.</param>
        /// <param name="pipeline">The pipeline to call based on what is read.</param>
        protected abstract void ReadGeography(TReaderStream readerStream, SpatialPipeline pipeline);

        /// <summary>
        /// Read the Geometry from the readerStream and call the appopriate pipeline methods
        /// </summary>
        /// <param name="readerStream">The stream to read from.</param>
        /// <param name="pipeline">The pipeline to call based on what is read.</param>
        protected abstract void ReadGeometry(TReaderStream readerStream, SpatialPipeline pipeline);

        /// <summary>
        /// Creates the builder that will be called by the parser to build the new type.
        /// </summary>
        /// <returns>the builder that was created.</returns>
        protected KeyValuePair<SpatialPipeline, IShapeProvider> MakeValidatingBuilder()
        {
            var builder = this.creator.CreateBuilder();
            var validator = this.creator.CreateValidator();
            validator.ChainTo(builder);
            return new KeyValuePair<SpatialPipeline, IShapeProvider>(validator, builder);
        }
    }
}
