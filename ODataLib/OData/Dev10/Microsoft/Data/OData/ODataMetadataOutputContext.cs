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

namespace Microsoft.Data.OData
{
    #region Namespaces
    using System;
    using System.Collections.Generic;
    using System.Diagnostics;
    using System.IO;
    using System.Text;
    using System.Xml;
    using Microsoft.Data.Edm;
    using Microsoft.Data.Edm.Csdl;
    using Microsoft.Data.Edm.Validation;
    using Microsoft.Data.OData.Atom;
    #endregion Namespaces

    /// <summary>
    /// RAW format output context.
    /// </summary>
    internal sealed class ODataMetadataOutputContext : ODataOutputContext
    {
        /// <summary>The message output stream.</summary>
        private Stream messageOutputStream;

        /// <summary>The XmlWriter to write to.</summary>
        private XmlWriter xmlWriter;

        /// <summary>
        /// Constructor.
        /// </summary>
        /// <param name="format">The format for this output context.</param>
        /// <param name="messageStream">The message stream to write the payload to.</param>
        /// <param name="encoding">The encoding to use for the payload.</param>
        /// <param name="messageWriterSettings">Configuration settings of the OData writer.</param>
        /// <param name="writingResponse">true if writing a response message; otherwise false.</param>
        /// <param name="synchronous">true if the output should be written synchronously; false if it should be written asynchronously.</param>
        /// <param name="model">The model to use.</param>
        /// <param name="urlResolver">The optional URL resolver to perform custom URL resolution for URLs written to the payload.</param>
        private ODataMetadataOutputContext(
            ODataFormat format,
            Stream messageStream,
            Encoding encoding,
            ODataMessageWriterSettings messageWriterSettings,
            bool writingResponse,
            bool synchronous,
            IEdmModel model,
            IODataUrlResolver urlResolver)
            : base(format, messageWriterSettings, writingResponse, synchronous, model, urlResolver)
        {
            Debug.Assert(synchronous, "Metadata output context is only supported in synchronous operations.");

            try
            {
                this.messageOutputStream = messageStream;
                this.xmlWriter = ODataAtomWriterUtils.CreateXmlWriter(messageStream, messageWriterSettings, encoding);
            }
            catch (Exception e)
            {
                // Dispose the message stream if we failed to create the output context.
                if (ExceptionUtils.IsCatchableExceptionType(e) && messageStream != null)
                {
                    messageStream.Dispose();
                }

                throw;
            }
        }

        /// <summary>
        /// Create metadata output context.
        /// </summary>
        /// <param name="format">The format to create the output context for.</param>
        /// <param name="message">The message to use.</param>
        /// <param name="encoding">The encoding to use.</param>
        /// <param name="messageWriterSettings">Configuration settings of the OData writer.</param>
        /// <param name="writingResponse">true if writing a response message; otherwise false.</param>
        /// <param name="model">The model to use.</param>
        /// <param name="urlResolver">The optional URL resolver to perform custom URL resolution for URLs written to the payload.</param>
        /// <returns>The newly created output context.</returns>
        internal static ODataOutputContext Create(
            ODataFormat format,
            ODataMessage message,
            Encoding encoding,
            ODataMessageWriterSettings messageWriterSettings,
            bool writingResponse,
            IEdmModel model,
            IODataUrlResolver urlResolver)
        {
            DebugUtils.CheckNoExternalCallers();
            Debug.Assert(format == ODataFormat.Metadata, "This method only supports the Metadata format.");
            Debug.Assert(message != null, "message != null");
            Debug.Assert(messageWriterSettings != null, "messageWriterSettings != null");

            Stream messageStream = message.GetStream();
            return new ODataMetadataOutputContext(
                format,
                messageStream,
                encoding,
                messageWriterSettings,
                writingResponse,
                true,
                model,
                urlResolver);
        }

        /// <summary>
        /// Synchronously flush the writer.
        /// </summary>
        internal void Flush()
        {
            DebugUtils.CheckNoExternalCallers();
            this.AssertSynchronous();

            // XmlWriter.Flush will call the underlying Stream.Flush.
            // In the synchronous case the underlying stream is the message stream itself, which will then Flush as well.
            this.xmlWriter.Flush();
        }

        /// <summary>
        /// Writes an <see cref="ODataError"/> into the message payload.
        /// </summary>
        /// <param name="error">The error to write.</param>
        /// <param name="includeDebugInformation">
        /// A flag indicating whether debug information (e.g., the inner error from the <paramref name="error"/>) should 
        /// be included in the payload. This should only be used in debug scenarios.
        /// </param>
        /// <remarks>
        /// This method is called if the ODataMessageWriter.WriteError is called once some other
        /// write operation has already started.
        /// The method should write the in-stream error representation for the specific format into the current payload.
        /// Before the method is called no flush is performed on the output context or any active writer.
        /// It is the responsibility of this method to flush the output before the method returns.
        /// </remarks>
        internal override void WriteInStreamError(ODataError error, bool includeDebugInformation)
        {
            DebugUtils.CheckNoExternalCallers();
            this.AssertSynchronous();

            ODataAtomWriterUtils.WriteError(this.xmlWriter, error, includeDebugInformation, this.MessageWriterSettings.MessageQuotas.MaxNestingDepth);
            this.Flush();
        }

        /// <summary>
        /// Writes the metadata document as the message body.
        /// </summary>
        /// <remarks>It is the responsibility of this method to flush the output before the method returns.</remarks>
        internal override void WriteMetadataDocument()
        {
            DebugUtils.CheckNoExternalCallers();
            this.AssertSynchronous();

            // Save the in-memory OData annotations to the model (i.e., turn them into serializable ones)
            this.Model.SaveODataAnnotations();

            IEnumerable<EdmError> errors;
            if (!EdmxWriter.TryWriteEdmx(this.Model, this.xmlWriter, EdmxTarget.OData, out errors))
            {
                Debug.Assert(errors != null, "errors != null");

                StringBuilder builder = new StringBuilder();
                foreach (EdmError error in errors)
                {
                    builder.AppendLine(error.ToString());
                }

                throw new ODataException(Strings.ODataMetadataOutputContext_ErrorWritingMetadata(builder.ToString()));
            }

            this.Flush();
        }

        /// <summary>
        /// Perform the actual cleanup work.
        /// </summary>
        /// <param name="disposing">If 'true' this method is called from user code; if 'false' it is called by the runtime.</param>
        protected override void Dispose(bool disposing)
        {
            base.Dispose(disposing);
            try
            {
                if (this.xmlWriter != null)
                {
                    // Note that the this.xmlWriter might be one of the XML customization writers, but since we don't own those
                    // we won't be flushing or disposing them. We just own the root writer which we created.

                    // XmlWriter.Flush will call the underlying Stream.Flush.
                    this.xmlWriter.Flush();

                    // XmlWriter.Dispose calls XmlWriter.Close which writes missing end elements.
                    // Thus we can't dispose the XmlWriter since that might end up writing more data into the stream right here
                    // and thus callers would have no way to prevent us from writing synchronously into the underlying stream.
                    // Also in case of in-stream error we intentionally want to not write the end elements to keep the payload invalid.
                    // In the async case the underlying stream is the async buffered stream, so we have to flush that explicitely.

                    // Dipose the message stream (note that we OWN this stream, so we always dispose it).
                    this.messageOutputStream.Dispose();
                }
            }
            finally
            {
                this.messageOutputStream = null;
                this.xmlWriter = null;
            }
        }
    }
}
