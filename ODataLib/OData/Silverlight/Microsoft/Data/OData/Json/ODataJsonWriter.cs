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

namespace Microsoft.Data.OData.Json
{
    #region Namespaces
    using System;
    using System.Diagnostics;
    using System.Diagnostics.CodeAnalysis;
    using System.IO;
    using System.Text;
#if ODATALIB_ASYNC
    using System.Threading.Tasks;
#endif
    using Microsoft.Data.Edm;
    using o = Microsoft.Data.OData;
    #endregion Namespaces

    /// <summary>
    /// Implementation of the ODataWriter for the JSON format.
    /// </summary>
    internal sealed class ODataJsonWriter : ODataWriterCore
    {
        /// <summary>
        /// The output context to write to.
        /// </summary>
        private readonly ODataJsonOutputContext jsonOutputContext;

        /// <summary>
        /// The JSON entry and feed seriazlizer to use.
        /// </summary>
        private readonly ODataJsonEntryAndFeedSerializer jsonEntryAndFeedSerializer;

        /// <summary>
        /// Constructor.
        /// </summary>
        /// <param name="jsonOutputContext">The output context to write to.</param>
        /// <param name="writingFeed">true if the writer is created for writing a feed; false when it is created for writing an entry.</param>
        internal ODataJsonWriter(ODataJsonOutputContext jsonOutputContext, bool writingFeed)
            : base(jsonOutputContext, writingFeed)
        {
            DebugUtils.CheckNoExternalCallers();
            Debug.Assert(jsonOutputContext != null, "jsonOutputContext != null");

            this.jsonOutputContext = jsonOutputContext;
            this.jsonEntryAndFeedSerializer = new ODataJsonEntryAndFeedSerializer(this.jsonOutputContext);
        }

        /// <summary>
        /// Returns the current JsonFeedScope.
        /// </summary>
        private JsonFeedScope CurrentFeedScope
        {
            get
            {
                JsonFeedScope currentJsonFeedScope = this.CurrentScope as JsonFeedScope;
                Debug.Assert(currentJsonFeedScope != null, "Asking for JsonFeedScope when the current scope is not a JsonFeedScope.");
                return currentJsonFeedScope;
            }
        }

        /// <summary>
        /// Check if the object has been disposed; called from all public API methods. Throws an ObjectDisposedException if the object
        /// has already been disposed.
        /// </summary>
        protected override void VerifyNotDisposed()
        {
            this.jsonOutputContext.VerifyNotDisposed();
        }

        /// <summary>
        /// Flush the output.
        /// </summary>
        protected override void FlushSynchronously()
        {
            this.jsonOutputContext.Flush();
        }

#if ODATALIB_ASYNC
        /// <summary>
        /// Flush the output.
        /// </summary>
        /// <returns>Task representing the pending flush operation.</returns>
        protected override Task FlushAsynchronously()
        {
            return this.jsonOutputContext.FlushAsync();
        }
#endif

        /// <summary>
        /// Starts writing a payload (called exactly once before anything else)
        /// </summary>
        protected override void StartPayload()
        {
            this.jsonEntryAndFeedSerializer.WritePayloadStart();
        }

        /// <summary>
        /// Ends writing a payload (called exactly once after everything else in case of success)
        /// </summary>
        protected override void EndPayload()
        {
            this.jsonEntryAndFeedSerializer.WritePayloadEnd();
        }

        /// <summary>
        /// Start writing an entry.
        /// </summary>
        /// <param name="entry">The entry to write.</param>
        protected override void StartEntry(ODataEntry entry)
        {
            if (entry == null)
            {
                Debug.Assert(
                    this.ParentNavigationLink != null && !this.ParentNavigationLink.IsCollection.Value,
                        "when entry == null, it has to be and expanded single entry navigation");

                // this is a null expanded single entry and it is null, so write a JSON null as value.
                this.jsonOutputContext.JsonWriter.WriteValue(null);
                return;
            }

            // Write just the object start, nothing else, since we might not have complete information yet
            this.jsonOutputContext.JsonWriter.StartObjectScope();

            // Get the projected properties
            ProjectedPropertiesAnnotation projectedProperties = entry.GetAnnotation<ProjectedPropertiesAnnotation>();

            // Write the metadata
            this.jsonEntryAndFeedSerializer.WriteEntryMetadata(entry, projectedProperties, this.EntryEntityType, this.DuplicatePropertyNamesChecker);

        }

        /// <summary>
        /// Finish writing an entry.
        /// </summary>
        /// <param name="entry">The entry to write.</param>
        protected override void EndEntry(ODataEntry entry)
        {
            if (entry == null)
            {
                Debug.Assert(
                    this.ParentNavigationLink != null && !this.ParentNavigationLink.IsCollection.Value,
                        "when entry == null, it has to be and expanded single entry navigation");

                // this is a null expanded single entry and it is null, JSON null should be written as value in StartEntry()
                return;
            }

            // Get the projected properties
            ProjectedPropertiesAnnotation projectedProperties = entry.GetAnnotation<ProjectedPropertiesAnnotation>();

            // Write the properties
            this.jsonEntryAndFeedSerializer.AssertRecursionDepthIsZero();
            this.jsonEntryAndFeedSerializer.WriteProperties(
                this.EntryEntityType,
                entry.Properties,
                false /* isComplexValue */,
                this.DuplicatePropertyNamesChecker,
                projectedProperties);
            this.jsonEntryAndFeedSerializer.AssertRecursionDepthIsZero();

            // Close the object scope
            this.jsonOutputContext.JsonWriter.EndObjectScope();
        }

        /// <summary>
        /// Start writing a feed.
        /// </summary>
        /// <param name="feed">The feed to write.</param>
        protected override void StartFeed(ODataFeed feed)
        {
            Debug.Assert(feed != null, "feed != null");

            // If we are writing a request and this is a feed in an expanded link, we must not write the array around the feed
            // since the navigation link already wrote it (as the array contains a mix of entity reference links and entries from the feed).
            if (this.ParentNavigationLink == null || this.jsonOutputContext.WritingResponse)
            {
                // According to OIPI feeds are allowed in requests and responses:
                //   v1 feeds can appear in requests and responses and don't have a 'results' wrapper
                //   >= v2 feeds can appear at the top level of responses and then have a 'results' wrapper
                //   >= v2 feeds can appear as expanded link content in responses and then have a 'results' wrapper 
                // NOTE: OIPI does not specify a format for top-level >= v2 feeds in requests; as a result we use the v1 format 
                //       for these (i.e., no 'results' wrapper)
                if (this.jsonOutputContext.Version >= ODataVersion.V2 && this.jsonOutputContext.WritingResponse)
                {
                    // {
                    this.jsonOutputContext.JsonWriter.StartObjectScope();

                    // "__count": "number"
                    // Write if it's available.
                    this.WriteFeedCount(feed);

                    // "results":
                    this.jsonOutputContext.JsonWriter.WriteDataArrayName();
                }

                // Start array which will hold the entries in the feed.
                this.jsonOutputContext.JsonWriter.StartArrayScope();
            }
            else
            {
                Debug.Assert(this.ParentNavigationLink.IsCollection.Value, "We should have verified that feeds can only be written into IsCollection = true links in requests.");
            }
        }

        /// <summary>
        /// Finish writing a feed.
        /// </summary>
        /// <param name="feed">The feed to write.</param>
        protected override void EndFeed(ODataFeed feed)
        {
            Debug.Assert(feed != null, "feed != null");

            // If we are writing a request and this is a feed in an expanded link, we must not write the array around the feed
            // since the navigation link already wrote it (as the array contains a mix of entity reference links and entries from the feed).
            if (this.ParentNavigationLink == null || this.jsonOutputContext.WritingResponse)
            {
                // End the array which holds the entries in the feed.
                this.jsonOutputContext.JsonWriter.EndArrayScope();

                // We need to close the "results" wrapper for V2 and higher and only for responses
                Uri nextPageLink = feed.NextPageLink;
                if (this.jsonOutputContext.Version >= ODataVersion.V2 && this.jsonOutputContext.WritingResponse)
                {
                    // "__count": "number"
                    // Will be written only if it hasn't been already in the StartFeed.
                    this.WriteFeedCount(feed);

                    // "__next": "url"
                    if (nextPageLink != null)
                    {
                        this.jsonOutputContext.JsonWriter.WriteName(JsonConstants.ODataNextLinkName);
                        this.jsonOutputContext.JsonWriter.WriteValue(this.jsonEntryAndFeedSerializer.UriToAbsoluteUriString(nextPageLink));
                    }

                    this.jsonOutputContext.JsonWriter.EndObjectScope();
                }
            }
            else
            {
                Debug.Assert(this.ParentNavigationLink.IsCollection.Value, "We should have verified that feeds can only be written into IsCollection = true links in requests.");
            }
        }

        /// <summary>
        /// Start writing a deferred (non-expanded) navigation link.
        /// </summary>
        /// <param name="navigationLink">The navigation link to write.</param>
        protected override void WriteDeferredNavigationLink(ODataNavigationLink navigationLink)
        {
            Debug.Assert(navigationLink != null, "navigationLink != null");
            Debug.Assert(this.jsonOutputContext.WritingResponse, "Deferred links are only supported in response, we should have verified this already.");

            // Link must specify the Url for non-expanded links
            // NOTE: we currently only require a non-null Url for ATOM payloads and non-expanded links in JSON.
            //       There is no place in JSON to write a Url if the navigation link is expanded. We can't change that for v1 and v2; we
            //       might fix the protocol for v3.
            if (navigationLink.Url == null)
            {
                throw new ODataException(o.Strings.ODataWriter_NavigationLinkMustSpecifyUrl);
            }

            Debug.Assert(!string.IsNullOrEmpty(navigationLink.Name), "The navigation link Name should have been validated by now.");
            this.jsonOutputContext.JsonWriter.WriteName(navigationLink.Name);

            // A deferred navigation link is represented as an object
            this.jsonOutputContext.JsonWriter.StartObjectScope();

            // "__deferred": {
            this.jsonOutputContext.JsonWriter.WriteName(JsonConstants.ODataDeferredName);
            this.jsonOutputContext.JsonWriter.StartObjectScope();

            Debug.Assert(navigationLink.Url != null, "The navigation link Url should have been validated by now.");
            this.jsonOutputContext.JsonWriter.WriteName(JsonConstants.ODataNavigationLinkUriName);
            this.jsonOutputContext.JsonWriter.WriteValue(this.jsonEntryAndFeedSerializer.UriToAbsoluteUriString(navigationLink.Url));

            // End the __deferred object
            this.jsonOutputContext.JsonWriter.EndObjectScope();

            // End the navigation link value
            this.jsonOutputContext.JsonWriter.EndObjectScope();
        }

        /// <summary>
        /// Start writing a navigation link with content.
        /// </summary>
        /// <param name="navigationLink">The navigation link to write.</param>
        protected override void StartNavigationLinkWithContent(ODataNavigationLink navigationLink)
        {
            Debug.Assert(navigationLink != null, "navigationLink != null");

            Debug.Assert(!string.IsNullOrEmpty(navigationLink.Name), "The navigation link name should have been verified by now.");
            this.jsonOutputContext.JsonWriter.WriteName(navigationLink.Name);

            if (this.jsonOutputContext.WritingResponse)
            {
                // In response, there will be exactly one 
                // Everything else is done in the expanded feed or entry since we include the link related information
                // in the feed/entry object.
            }
            else
            {
                if (navigationLink.IsCollection == null)
                {
                    throw new ODataException(o.Strings.ODataWriterCore_LinkMustSpecifyIsCollection);
                }

                // In request, the navigation link may have multiple items in it, so we need to write the array around it here, if it's a collection
                // For singletons, there's no wrapper object/array to write anyway.
                if (navigationLink.IsCollection.Value)
                {
                    this.jsonOutputContext.JsonWriter.StartArrayScope();
                }
            }

        }

        /// <summary>
        /// Finish writing a navigation link with content.
        /// </summary>
        /// <param name="navigationLink">The navigation link to write.</param>
        protected override void EndNavigationLinkWithContent(ODataNavigationLink navigationLink)
        {
            Debug.Assert(navigationLink != null, "navigationLink != null");

            if (this.jsonOutputContext.WritingResponse)
            {
                // Nothing to do here, the navigation link is represented as a JSON object which is either the feed or entry
            }
            else
            {
                // In request, if the navigation link is a collection we must close the array we've started.
                if (navigationLink.IsCollection.Value)
                {
                    this.jsonOutputContext.JsonWriter.EndArrayScope();
                }
            }
        }

        /// <summary>
        /// Write an entity reference link.
        /// </summary>
        /// <param name="parentNavigationLink">The parent navigation link which is being written around the entity reference link.</param>
        /// <param name="entityReferenceLink">The entity reference link to write.</param>
        protected override void WriteEntityReferenceInNavigationLinkContent(ODataNavigationLink parentNavigationLink, ODataEntityReferenceLink entityReferenceLink)
        {
            Debug.Assert(parentNavigationLink != null, "parentNavigationLink != null");
            Debug.Assert(entityReferenceLink != null, "entityReferenceLink != null");
            Debug.Assert(!this.jsonOutputContext.WritingResponse, "Entity reference links are only supported in request, we should have verified this already.");

            // An entity reference link is represented as an object
            this.jsonOutputContext.JsonWriter.StartObjectScope();

            // "__metadata": {
            this.jsonOutputContext.JsonWriter.WriteName(JsonConstants.ODataMetadataName);
            this.jsonOutputContext.JsonWriter.StartObjectScope();

            Debug.Assert(entityReferenceLink.Url != null, "The entity reference link Url should have been validated by now.");
            this.jsonOutputContext.JsonWriter.WriteName(JsonConstants.ODataMetadataUriName);
            this.jsonOutputContext.JsonWriter.WriteValue(this.jsonEntryAndFeedSerializer.UriToAbsoluteUriString(entityReferenceLink.Url));

            // End the __metadata object
            this.jsonOutputContext.JsonWriter.EndObjectScope();

            // End the entity reference link value
            this.jsonOutputContext.JsonWriter.EndObjectScope();
        }

        /// <summary>
        /// Create a new feed scope.
        /// </summary>
        /// <param name="feed">The feed for the new scope.</param>
        /// <param name="skipWriting">true if the content of the scope to create should not be written.</param>
        /// <returns>The newly create scope.</returns>
        protected override FeedScope CreateFeedScope(ODataFeed feed, bool skipWriting)
        {
            return new JsonFeedScope(feed, skipWriting);
        }

        /// <summary>
        /// Create a new entry scope.
        /// </summary>
        /// <param name="entry">The entry for the new scope.</param>
        /// <param name="skipWriting">true if the content of the scope to create should not be written.</param>
        /// <returns>The newly create scope.</returns>
        protected override EntryScope CreateEntryScope(ODataEntry entry, bool skipWriting)
        {
            return new EntryScope(entry, skipWriting, this.jsonOutputContext.WritingResponse, this.jsonOutputContext.MessageWriterSettings.WriterBehavior);
        }

        /// <summary>
        /// Writes the __count property for a feed if it has not been written yet (and the count is specified on the feed).
        /// </summary>
        /// <param name="feed">The feed to write the count for.</param>
        private void WriteFeedCount(ODataFeed feed)
        {
            Debug.Assert(feed != null, "feed != null");

            // If we haven't written the count yet and it's available, write it.
            long? count = feed.Count;
            if (count.HasValue && !this.CurrentFeedScope.CountWritten)
            {
                this.jsonOutputContext.JsonWriter.WriteName(JsonConstants.ODataCountName);

                this.jsonOutputContext.JsonWriter.WriteValue(count.Value);
                this.CurrentFeedScope.CountWritten = true;
            }
        }

        /// <summary>
        /// A scope for an feed.
        /// </summary>
        private sealed class JsonFeedScope : FeedScope
        {
            /// <summary>true if the __count was already written, false otherwise.</summary>
            private bool countWritten;

            /// <summary>
            /// Constructor to create a new feed scope.
            /// </summary>
            /// <param name="feed">The feed for the new scope.</param>
            /// <param name="skipWriting">true if the content of the scope to create should not be written.</param>
            internal JsonFeedScope(ODataFeed feed, bool skipWriting)
                : base(feed, skipWriting)
            {
                DebugUtils.CheckNoExternalCallers();
            }

            /// <summary>
            /// true if the __count was already written, false otherwise.
            /// </summary>
            internal bool CountWritten
            {
                get
                {
                    DebugUtils.CheckNoExternalCallers();
                    return this.countWritten;
                }

                set
                {
                    DebugUtils.CheckNoExternalCallers();
                    this.countWritten = value;
                }
            }
        }
    }
}
