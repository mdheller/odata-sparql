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

namespace Microsoft.Data.OData.Atom
{
    #region Namespaces
    using System;
    using System.Collections.Generic;
    using System.Diagnostics;
    using System.Diagnostics.CodeAnalysis;
    using System.IO;
    using System.Text;
#if ODATALIB_ASYNC
    using System.Threading.Tasks;
#endif
    using System.Xml;
    using Microsoft.Data.Edm;
    using Microsoft.Data.Edm.Library;
    using Microsoft.Data.OData.Metadata;
    using o = Microsoft.Data.OData;
    #endregion Namespaces

    /// <summary>
    /// OData writer for the ATOM format.
    /// </summary>
    internal sealed class ODataAtomWriter : ODataWriterCore
    {
        /// <summary>Value for the atom:updated element.</summary>
        /// <remarks>
        /// The writer will use the same default value for the atom:updated element in a given payload. While there is no requirement for this,
        /// it saves us from re-querying the system time and converting it to string every time we write an item.
        /// </remarks>
        private readonly string updatedTime = ODataAtomConvert.ToAtomString(DateTimeOffset.UtcNow);

        /// <summary>The output context to write to.</summary>
        private readonly ODataAtomOutputContext atomOutputContext;

        /// <summary>The serializer to write payload with.</summary>
        private readonly ODataAtomEntryAndFeedSerializer atomEntryAndFeedSerializer;

        /// <summary>
        /// Constructor creating an OData writer using the ATOM format.
        /// </summary>
        /// <param name="atomOutputContext">The output context to write to.</param>
        /// <param name="writingFeed">True if the writer is created for writing a feed; false when it is created for writing an entry.</param>
        internal ODataAtomWriter(ODataAtomOutputContext atomOutputContext, bool writingFeed)
            : base(atomOutputContext, writingFeed)
        {
            DebugUtils.CheckNoExternalCallers();
            Debug.Assert(atomOutputContext != null, "atomOutputContext != null");

            this.atomOutputContext = atomOutputContext;

            if (this.atomOutputContext.MessageWriterSettings.AtomStartEntryXmlCustomizationCallback != null)
            {
                Debug.Assert(
                    this.atomOutputContext.MessageWriterSettings.AtomEndEntryXmlCustomizationCallback != null,
                    "We should have verified that both start end end XML customization callbacks are specified.");
                this.atomOutputContext.InitializeWriterCustomization();
            }

            this.atomEntryAndFeedSerializer = new ODataAtomEntryAndFeedSerializer(this.atomOutputContext);
        }

        /// <summary>
        /// Enumeration of ATOM element flags, used to keep track of which elements were already written.
        /// </summary>
        private enum AtomElement
        {
            /// <summary>The atom:id element.</summary>
            Id = 0x1,

            /// <summary>The atom:link with rel='self'.</summary>
            ReadLink = 0x2,

            /// <summary>The atom:link with rel='edit'.</summary>
            EditLink = 0x4,
        }

        /// <summary>
        /// Returns the current AtomEntryScope.
        /// </summary>
        private AtomEntryScope CurrentEntryScope
        {
            get
            {
                AtomEntryScope currentAtomEntryScope = this.CurrentScope as AtomEntryScope;
                Debug.Assert(currentAtomEntryScope != null, "Asking for AtomEntryScope when the current scope is not an AtomEntryScope.");
                return currentAtomEntryScope;
            }
        }

        /// <summary>
        /// Returns the current AtomFeedScope.
        /// </summary>
        private AtomFeedScope CurrentFeedScope
        {
            get
            {
                AtomFeedScope currentAtomFeedScope = this.CurrentScope as AtomFeedScope;
                Debug.Assert(currentAtomFeedScope != null, "Asking for AtomFeedScope when the current scope is not an AtomFeedScope.");
                return currentAtomFeedScope;
            }
        }

        /// <summary>
        /// Check if the object has been disposed; called from all public API methods. Throws an ObjectDisposedException if the object
        /// has already been disposed.
        /// </summary>
        protected override void VerifyNotDisposed()
        {
            this.atomOutputContext.VerifyNotDisposed();
        }

        /// <summary>
        /// Flush the output.
        /// </summary>
        protected override void FlushSynchronously()
        {
            this.atomOutputContext.Flush();
        }

#if ODATALIB_ASYNC
        /// <summary>
        /// Flush the output.
        /// </summary>
        /// <returns>Task representing the pending flush operation.</returns>
        protected override Task FlushAsynchronously()
        {
            return this.atomOutputContext.FlushAsync();
        }
#endif

        /// <summary>
        /// Start writing an OData payload.
        /// </summary>
        protected override void StartPayload()
        {
            this.atomEntryAndFeedSerializer.WritePayloadStart();
        }

        /// <summary>
        /// Finish writing an OData payload.
        /// </summary>
        protected override void EndPayload()
        {
            this.atomEntryAndFeedSerializer.WritePayloadEnd();
        }

        /// <summary>
        /// Start writing an entry.
        /// </summary>
        /// <param name="entry">The entry to write.</param>
        protected override void StartEntry(ODataEntry entry)
        {
            this.CheckAndWriteParentNavigationLinkStartForInlineElement();
            Debug.Assert(
                this.ParentNavigationLink == null || !this.ParentNavigationLink.IsCollection.Value,
                "We should have already verified that the IsCollection matches the actual content of the link (feed/entry).");

            if (entry == null)
            {
                Debug.Assert(this.ParentNavigationLink != null, "When entry == null, it has to be an expanded single entry navigation.");

                // this is a null expanded single entry and it is null, an empty <m:inline /> will be written.
                return;
            }

            this.StartEntryXmlCustomization(entry);

            // <entry>
            this.atomOutputContext.XmlWriter.WriteStartElement(AtomConstants.AtomNamespacePrefix, AtomConstants.AtomEntryElementName, AtomConstants.AtomNamespace);

            if (this.IsTopLevel)
            {
                this.atomEntryAndFeedSerializer.WriteBaseUriAndDefaultNamespaceAttributes();
            }

            string etag = entry.ETag;
            if (etag != null)
            {
                // TODO, ckerer: if this is a top-level entry also put the ETag into the headers.
                ODataAtomWriterUtils.WriteETag(this.atomOutputContext.XmlWriter, etag);
            }

            AtomEntryScope currentEntryScope = this.CurrentEntryScope;
            AtomEntryMetadata entryMetadata = entry.Atom();

            // Write the id if it's available here.
            // If it's not available here we will try to write it at the end of the entry again.
            string entryId = entry.Id;
            if (entryId != null)
            {
                this.atomEntryAndFeedSerializer.WriteEntryId(entryId);
                currentEntryScope.SetWrittenElement(AtomElement.Id);
            }

            // <category term="type" scheme="odatascheme"/>
            // If no type information is provided, don't include the category element for type at all
            // NOTE: the validation of the type name is done by the core writer.
            string typeName = entry.TypeName;
            SerializationTypeNameAnnotation serializationTypeNameAnnotation = entry.GetAnnotation<SerializationTypeNameAnnotation>();
            if (serializationTypeNameAnnotation != null)
            {
                typeName = serializationTypeNameAnnotation.TypeName;
            }

            this.atomEntryAndFeedSerializer.WriteEntryTypeName(typeName, entryMetadata);

            // Write the edit link if it's available here.
            // If it's not available here we will try to write it at the end of the entry again.
            Uri editLink = entry.EditLink;
            if (editLink != null)
            {
                this.atomEntryAndFeedSerializer.WriteEntryEditLink(editLink, entryMetadata);
                currentEntryScope.SetWrittenElement(AtomElement.EditLink);
            }

            // Write the self link if it's available here.
            // If it's not available here we will try to write it at the end of the entry again.
            Uri readLink = entry.ReadLink;
            if (readLink != null)
            {
                this.atomEntryAndFeedSerializer.WriteEntryReadLink(readLink, entryMetadata);
                currentEntryScope.SetWrittenElement(AtomElement.ReadLink);
            }
        }

        /// <summary>
        /// Finish writing an entry.
        /// </summary>
        /// <param name="entry">The entry to write.</param>
        [SuppressMessage("Microsoft.Maintainability", "CA1506:AvoidExcessiveClassCoupling", Justification = "The coupling is intentional here.")]
        protected override void EndEntry(ODataEntry entry)
        {
            Debug.Assert(
                this.ParentNavigationLink == null || !this.ParentNavigationLink.IsCollection.Value,
                "We should have already verified that the IsCollection matches the actual content of the link (feed/entry).");

            if (entry == null)
            {
                Debug.Assert(this.ParentNavigationLink != null, "When entry == null, it has to be an expanded single entry navigation.");

                // this is a null expanded single entry and it is null, an empty <m:inline /> will be written.
                this.CheckAndWriteParentNavigationLinkEndForInlineElement();
                return;
            }

            IEdmEntityType entryType = this.EntryEntityType;

            // Initialize the property value cache and cache the entry properties.
            EntryPropertiesValueCache propertyValueCache = new EntryPropertiesValueCache(entry);

            // NOTE: when writing, we assume the model has been validated already and thus pass int.MaxValue for the maxMappingCount.
            ODataEntityPropertyMappingCache epmCache = this.atomOutputContext.Model.EnsureEpmCache(entryType, /*maxMappingCount*/ int.MaxValue);

            // Populate the property value cache based on the EPM source tree information.
            // We do this since we need to write custom EPM after the properties below and don't
            // want to cache all properties just for the case that they are used in a custom EPM.
            if (epmCache != null)
            {
                EpmWriterUtils.CacheEpmProperties(propertyValueCache, epmCache.EpmSourceTree);
            }

            // Get the projected properties annotation
            ProjectedPropertiesAnnotation projectedProperties = entry.GetAnnotation<ProjectedPropertiesAnnotation>();

            AtomEntryScope currentEntryScope = this.CurrentEntryScope;
            AtomEntryMetadata entryMetadata = entry.Atom();

            if (!currentEntryScope.IsElementWritten(AtomElement.Id))
            {
                // NOTE: We write even null id, in that case we generate an empty atom:id element.
                this.atomEntryAndFeedSerializer.WriteEntryId(entry.Id);
            }

            Uri editLink = entry.EditLink;
            if (editLink != null && !currentEntryScope.IsElementWritten(AtomElement.EditLink))
            {
                this.atomEntryAndFeedSerializer.WriteEntryEditLink(editLink, entryMetadata);
            }

            Uri readLink = entry.ReadLink;
            if (readLink != null && !currentEntryScope.IsElementWritten(AtomElement.ReadLink))
            {
                this.atomEntryAndFeedSerializer.WriteEntryReadLink(readLink, entryMetadata);
            }

            // write entry metadata including syndication EPM
            AtomEntryMetadata epmEntryMetadata = null;
            if (epmCache != null)
            {
                ODataVersionChecker.CheckEntityPropertyMapping(this.atomOutputContext.Version, entryType, this.atomOutputContext.Model);

                epmEntryMetadata = EpmSyndicationWriter.WriteEntryEpm(
                    epmCache.EpmTargetTree,
                    propertyValueCache,
                    entryType.ToTypeReference().AsEntity(),
                    this.atomOutputContext);
            }

            this.atomEntryAndFeedSerializer.WriteEntryMetadata(entryMetadata, epmEntryMetadata, this.updatedTime);

            // stream properties
            IEnumerable<ODataProperty> streamProperties = propertyValueCache.EntryStreamProperties;
            if (streamProperties != null)
            {
                foreach (ODataProperty streamProperty in streamProperties)
                {
                    this.atomEntryAndFeedSerializer.WriteStreamProperty(
                        streamProperty, 
                        entryType, 
                        this.DuplicatePropertyNamesChecker, 
                        projectedProperties);
                }
            }

            // association links
            IEnumerable<ODataAssociationLink> associationLinks = entry.AssociationLinks;
            if (associationLinks != null)
            {
                foreach (ODataAssociationLink associationLink in associationLinks)
                {
                    this.atomEntryAndFeedSerializer.WriteAssociationLink(
                        associationLink,
                        entryType,
                        this.DuplicatePropertyNamesChecker,
                        projectedProperties);
                }
            }

            // actions
            IEnumerable<ODataAction> actions = entry.Actions;
            if (actions != null)
            {
                foreach (ODataAction action in actions)
                {
                    ValidationUtils.ValidateOperationNotNull(action, true);
                    this.atomEntryAndFeedSerializer.WriteOperation(action);
                }
            }

            // functions
            IEnumerable<ODataFunction> functions = entry.Functions;
            if (functions != null)
            {
                foreach (ODataFunction function in functions)
                {
                    ValidationUtils.ValidateOperationNotNull(function, false);
                    this.atomEntryAndFeedSerializer.WriteOperation(function);
                }
            }

            // write the content
            this.WriteEntryContent(
                entry, 
                entryType, 
                propertyValueCache, 
                epmCache == null ? null : epmCache.EpmSourceTree.Root,
                projectedProperties);

            // write custom EPM
            if (epmCache != null)
            {
                EpmCustomWriter.WriteEntryEpm(
                    this.atomOutputContext.XmlWriter,
                    epmCache.EpmTargetTree,
                    propertyValueCache,
                    entryType.ToTypeReference().AsEntity(),
                    this.atomOutputContext);
            }

            // </entry>
            this.atomOutputContext.XmlWriter.WriteEndElement();

            this.EndEntryXmlCustomization(entry);

            this.CheckAndWriteParentNavigationLinkEndForInlineElement();
        }

        /// <summary>
        /// Start writing a feed.
        /// </summary>
        /// <param name="feed">The feed to write.</param>
        protected override void StartFeed(ODataFeed feed)
        {
            Debug.Assert(feed != null, "feed != null");
            Debug.Assert(
                this.ParentNavigationLink == null || !this.ParentNavigationLink.IsCollection.HasValue || this.ParentNavigationLink.IsCollection.Value,
                "We should have already verified that the IsCollection matches the actual content of the link (feed/entry).");

            this.CheckAndWriteParentNavigationLinkStartForInlineElement();

            // <atom:feed>
            this.atomOutputContext.XmlWriter.WriteStartElement(AtomConstants.AtomNamespacePrefix, AtomConstants.AtomFeedElementName, AtomConstants.AtomNamespace);

            if (this.IsTopLevel)
            {
                this.atomEntryAndFeedSerializer.WriteBaseUriAndDefaultNamespaceAttributes();

                if (feed.Count.HasValue)
                {
                    this.atomEntryAndFeedSerializer.WriteCount(feed.Count.Value, false);
                }
            }

            bool authorWritten;
            this.atomEntryAndFeedSerializer.WriteFeedMetadata(feed, this.updatedTime, out authorWritten);
            this.CurrentFeedScope.AuthorWritten = authorWritten;
        }

        /// <summary>
        /// Finish writing a feed.
        /// </summary>
        /// <param name="feed">The feed to write.</param>
        protected override void EndFeed(ODataFeed feed)
        {
            Debug.Assert(feed != null, "feed != null");
            Debug.Assert(
                this.ParentNavigationLink == null || this.ParentNavigationLink.IsCollection.Value,
                "We should have already verified that the IsCollection matches the actual content of the link (feed/entry).");

            AtomFeedScope currentFeedScope = this.CurrentFeedScope;
            if (!currentFeedScope.AuthorWritten && currentFeedScope.EntryCount == 0)
            {
                // Write an empty author if there were no entries, since the feed must have an author if the entries don't have one as per ATOM spec
                this.atomEntryAndFeedSerializer.WriteFeedDefaultAuthor();
            }

            this.atomEntryAndFeedSerializer.WriteFeedNextPageLink(feed);

            // </atom:feed>
            this.atomOutputContext.XmlWriter.WriteEndElement();

            this.CheckAndWriteParentNavigationLinkEndForInlineElement();
        }

        /// <summary>
        /// Start writing a navigation link.
        /// </summary>
        /// <param name="navigationLink">The navigation link to write.</param>
        protected override void WriteDeferredNavigationLink(ODataNavigationLink navigationLink)
        {
            Debug.Assert(navigationLink != null, "navigationLink != null");
            Debug.Assert(this.atomOutputContext.WritingResponse, "Deferred links are only supported in response, we should have verified this already.");

            this.WriteNavigationLinkStart(navigationLink, null);
            this.WriteNavigationLinkEnd();
        }

        /// <summary>
        /// Start writing a navigation link with content.
        /// </summary>
        /// <param name="navigationLink">The navigation link to write.</param>
        protected override void StartNavigationLinkWithContent(ODataNavigationLink navigationLink)
        {
            Debug.Assert(navigationLink != null, "navigationLink != null");

            // In requests, a navigation link can have multiple items in its content (in the OM view), either entity reference links or expanded entry/feed.
            // For each of these we need to write a separate atom:link element. So we can't write the start of the atom:link element here
            // instead we postpone writing it till the first item in the content.
            // In response, only one item can occur, but for simplicity we will keep the behavior the same as for request and thus postpone writing the atom:link
            // start element as well.
            // Note that the writer core guarantees that this method (and the matching EndNavigationLinkWithContent) is only called for navigation links
            // which actually have some content. The only case where navigation link doesn't have a content is in response, in which case this method won't
            // be called, instead the WriteDeferredNavigationLink is called.
        }

        /// <summary>
        /// Finish writing a navigation link with content.
        /// </summary>
        /// <param name="navigationLink">The navigation link to write.</param>
        protected override void EndNavigationLinkWithContent(ODataNavigationLink navigationLink)
        {
            Debug.Assert(navigationLink != null, "navigationLink != null");

            // We do not write the end element for atom:link here, since we need to write it for each item in the content separately.
            // See the detailed description in the StartNavigationLinkWithContent for details.
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
            Debug.Assert(entityReferenceLink.Url != null, "We should have already verifies that the Url specified on the entity reference link is not null.");

            this.WriteNavigationLinkStart(parentNavigationLink, entityReferenceLink.Url);
            this.WriteNavigationLinkEnd();
        }

        /// <summary>
        /// Create a new feed scope.
        /// </summary>
        /// <param name="feed">The feed for the new scope.</param>
        /// <param name="skipWriting">true if the content of the scope to create should not be written.</param>
        /// <returns>The newly create scope.</returns>
        protected override FeedScope CreateFeedScope(ODataFeed feed, bool skipWriting)
        {
            return new AtomFeedScope(feed, skipWriting);
        }

        /// <summary>
        /// Create a new entry scope.
        /// </summary>
        /// <param name="entry">The entry for the new scope.</param>
        /// <param name="skipWriting">true if the content of the scope to create should not be written.</param>
        /// <returns>The newly create scope.</returns>
        protected override EntryScope CreateEntryScope(ODataEntry entry, bool skipWriting)
        {
            return new AtomEntryScope(entry, skipWriting, this.atomOutputContext.WritingResponse, this.atomOutputContext.MessageWriterSettings.WriterBehavior);
        }

        /// <summary>
        /// Write the content of the given entry.
        /// </summary>
        /// <param name="entry">The entry for which to write properties.</param>
        /// <param name="entryType">The <see cref="IEdmEntityType"/> of the entry (or null if not metadata is available).</param>
        /// <param name="propertiesValueCache">The cache of properties.</param>
        /// <param name="rootSourcePathSegment">The root of the EPM source tree, if there's an EPM applied.</param>
        /// <param name="projectedProperties">Set of projected properties, or null if all properties should be written.</param>
        private void WriteEntryContent(
            ODataEntry entry,
            IEdmEntityType entryType, 
            EntryPropertiesValueCache propertiesValueCache, 
            EpmSourcePathSegment rootSourcePathSegment,
            ProjectedPropertiesAnnotation projectedProperties)
        {
            Debug.Assert(entry != null, "entry != null");
            Debug.Assert(propertiesValueCache != null, "propertiesValueCache != null");

            ODataStreamReferenceValue mediaResource = entry.MediaResource;
            if (mediaResource == null)
            {
                // <content type="application/xml">
                this.atomOutputContext.XmlWriter.WriteStartElement(
                    AtomConstants.AtomNamespacePrefix,
                    AtomConstants.AtomContentElementName,
                    AtomConstants.AtomNamespace);

                this.atomOutputContext.XmlWriter.WriteAttributeString(
                    AtomConstants.AtomTypeAttributeName,
                    MimeConstants.MimeApplicationXml);

                this.atomEntryAndFeedSerializer.AssertRecursionDepthIsZero();
                this.atomEntryAndFeedSerializer.WriteProperties(
                    entryType,
                    propertiesValueCache.EntryProperties,
                    false /* isWritingCollection */,
                    this.atomEntryAndFeedSerializer.WriteEntryPropertiesStart,
                    this.atomEntryAndFeedSerializer.WriteEntryPropertiesEnd,
                    this.DuplicatePropertyNamesChecker,
                    propertiesValueCache,
                    rootSourcePathSegment,
                    projectedProperties);
                this.atomEntryAndFeedSerializer.AssertRecursionDepthIsZero();

                // </content>
                this.atomOutputContext.XmlWriter.WriteEndElement();
            }
            else
            {
                WriterValidationUtils.ValidateStreamReferenceValue(mediaResource, true);

                this.atomEntryAndFeedSerializer.WriteEntryMediaEditLink(mediaResource);

                if (mediaResource.ReadLink != null)
                {
                    // <content type="type" src="src">
                    this.atomOutputContext.XmlWriter.WriteStartElement(
                        AtomConstants.AtomNamespacePrefix,
                        AtomConstants.AtomContentElementName,
                        AtomConstants.AtomNamespace);

                    Debug.Assert(!string.IsNullOrEmpty(mediaResource.ContentType), "The default stream content type should have been validated by now. If we have a read link we must have a non-empty content type as well.");
                    this.atomOutputContext.XmlWriter.WriteAttributeString(
                        AtomConstants.AtomTypeAttributeName,
                        mediaResource.ContentType);

                    this.atomOutputContext.XmlWriter.WriteAttributeString(
                        AtomConstants.MediaLinkEntryContentSourceAttributeName,
                        this.atomEntryAndFeedSerializer.UriToUrlAttributeValue(mediaResource.ReadLink));

                    // </content>
                    this.atomOutputContext.XmlWriter.WriteEndElement();
                }

                this.atomEntryAndFeedSerializer.AssertRecursionDepthIsZero();
                this.atomEntryAndFeedSerializer.WriteProperties(
                    entryType,
                    propertiesValueCache.EntryProperties, 
                    false /* isWritingCollection */,
                    this.atomEntryAndFeedSerializer.WriteEntryPropertiesStart,
                    this.atomEntryAndFeedSerializer.WriteEntryPropertiesEnd,
                    this.DuplicatePropertyNamesChecker,
                    propertiesValueCache,
                    rootSourcePathSegment,
                    projectedProperties);
                this.atomEntryAndFeedSerializer.AssertRecursionDepthIsZero();
            }
        }

        /// <summary>
        /// Writes the navigation link start atom:link element including the m:inline element if there's a parent navigation link.
        /// </summary>
        private void CheckAndWriteParentNavigationLinkStartForInlineElement()
        {
            Debug.Assert(this.State == WriterState.Entry || this.State == WriterState.Feed, "Only entry or feed can be written into a link with inline.");

            ODataNavigationLink parentNavigationLink = this.ParentNavigationLink;
            if (parentNavigationLink != null)
            {
                // We postponed writing the surrounding atom:link and m:inline until now since in request a single navigation link can have
                // multiple items in its content, each of which is written as a standalone atom:link. Thus the StartNavigationLinkWithContent is only called
                // once, but we may need to write multiple atom:link elements.

                // write the start element of the link
                this.WriteNavigationLinkStart(parentNavigationLink, null);

                // <m:inline>
                this.atomOutputContext.XmlWriter.WriteStartElement(
                    AtomConstants.ODataMetadataNamespacePrefix,
                    AtomConstants.ODataInlineElementName,
                    AtomConstants.ODataMetadataNamespace);
            }
        }

        /// <summary>
        /// Writes the navigation link end m:inline and end atom:link elements if there's a parent navigation link.
        /// </summary>
        private void CheckAndWriteParentNavigationLinkEndForInlineElement()
        {
            Debug.Assert(this.State == WriterState.Entry || this.State == WriterState.Feed, "Only entry or feed can be written into a link with inline.");

            ODataNavigationLink parentNavigationLink = this.ParentNavigationLink;
            if (parentNavigationLink != null)
            {
                // We postponed writing the surrounding atom:link and m:inline until now since in request a single navigation link can have
                // multiple items in its content, each of which is written as a standalone atom:link. Thus the EndNavigationLinkWithContent is only called
                // once, but we may need to write multiple atom:link elements.

                // </m:inline>
                this.atomOutputContext.XmlWriter.WriteEndElement();

                // </atom:link>
                this.WriteNavigationLinkEnd();
            }
        }

        /// <summary>
        /// Writes the navigation link's start element and atom metadata.
        /// </summary>
        /// <param name="navigationLink">The navigation link to write.</param>
        /// <param name="navigationLinkUrlOverride">Url to use for the navigation link. If this is specified the Url property on the <paramref name="navigationLink"/>
        /// will be ignored. If this parameter is null, the Url from the navigation link is used.</param>
        private void WriteNavigationLinkStart(ODataNavigationLink navigationLink, Uri navigationLinkUrlOverride)
        {
            // IsCollection is required for ATOM
            if (!navigationLink.IsCollection.HasValue)
            {
                throw new ODataException(o.Strings.ODataWriterCore_LinkMustSpecifyIsCollection);
            }

            // Navigation link must specify the Url
            // NOTE: we currently only require a non-null Url for ATOM payloads and non-expanded navigation links in JSON.
            //       There is no place in JSON to write a Url if the navigation link is expanded. We can't change that for v1 and v2; we
            //       might fix the protocol for v3.
            if (navigationLink.Url == null)
            {
                throw new ODataException(o.Strings.ODataWriter_NavigationLinkMustSpecifyUrl);
            }

            this.atomEntryAndFeedSerializer.WriteNavigationLinkStart(navigationLink, navigationLinkUrlOverride);
        }

        /// <summary>
        /// Writes custom extensions and the end element for a navigation link
        /// </summary>
        private void WriteNavigationLinkEnd()
        {
            // </atom:link>
            this.atomOutputContext.XmlWriter.WriteEndElement();
        }

        /// <summary>
        /// Determines if XML customization should be applied to the entry and applies it.
        /// </summary>
        /// <param name="entry">The entry to apply the customization to.</param>
        /// <remarks>This method must be called before anything is written for the entry in question.</remarks>
        private void StartEntryXmlCustomization(ODataEntry entry)
        {
            // If we are to customize the XML, replace the writers here:
            if (this.atomOutputContext.MessageWriterSettings.AtomStartEntryXmlCustomizationCallback != null)
            {
                XmlWriter entryWriter = this.atomOutputContext.MessageWriterSettings.AtomStartEntryXmlCustomizationCallback(
                    entry,
                    this.atomOutputContext.XmlWriter);
                if (entryWriter != null)
                {
                    if (object.ReferenceEquals(this.atomOutputContext.XmlWriter, entryWriter))
                    {
                        throw new ODataException(o.Strings.ODataAtomWriter_StartEntryXmlCustomizationCallbackReturnedSameInstance);
                    }
                }
                else
                {
                    entryWriter = this.atomOutputContext.XmlWriter;
                }

                this.atomOutputContext.PushCustomWriter(entryWriter);
            }
        }

        /// <summary>
        /// Ends XML customization for the entry (if one was applied).
        /// </summary>
        /// <param name="entry">The entry to end the customization for.</param>
        /// <remarks>This method must be called after all the XML for a given entry is written.</remarks>
        private void EndEntryXmlCustomization(ODataEntry entry)
        {
            if (this.atomOutputContext.MessageWriterSettings.AtomStartEntryXmlCustomizationCallback != null)
            {
                XmlWriter entryWriter = this.atomOutputContext.PopCustomWriter();
                if (!object.ReferenceEquals(this.atomOutputContext.XmlWriter, entryWriter))
                {
                    this.atomOutputContext.MessageWriterSettings.AtomEndEntryXmlCustomizationCallback(entry, entryWriter, this.atomOutputContext.XmlWriter);
                }
            }
        }

        /// <summary>
        /// A scope for an feed.
        /// </summary>
        private sealed class AtomFeedScope : FeedScope
        {
            /// <summary>true if the author element was already written, false otherwise.</summary>
            private bool authorWritten;

            /// <summary>
            /// Constructor to create a new feed scope.
            /// </summary>
            /// <param name="feed">The feed for the new scope.</param>
            /// <param name="skipWriting">true if the content of the scope to create should not be written.</param>
            internal AtomFeedScope(ODataFeed feed, bool skipWriting)
                : base(feed, skipWriting)
            {
                DebugUtils.CheckNoExternalCallers();
            }

            /// <summary>
            /// true if the author element was already written, false otherwise.
            /// </summary>
            internal bool AuthorWritten
            {
                get
                {
                    DebugUtils.CheckNoExternalCallers();
                    return this.authorWritten;
                }

                set
                {
                    DebugUtils.CheckNoExternalCallers();
                    this.authorWritten = value;
                }
            }
        }

        /// <summary>
        /// A scope for an entry in ATOM writer.
        /// </summary>
        private sealed class AtomEntryScope : EntryScope
        {
            /// <summary>Bit field of the ATOM elements written so far.</summary>
            private int alreadyWrittenElements;

            /// <summary>
            /// Constructor to create a new entry scope.
            /// </summary>
            /// <param name="entry">The entry for the new scope.</param>
            /// <param name="skipWriting">true if the content of the scope to create should not be written.</param>
            /// <param name="writingResponse">true if we are writing a response, false if it's a request.</param>
            /// <param name="writerBehavior">The <see cref="ODataWriterBehavior"/> instance controlling the behavior of the writer.</param>
            internal AtomEntryScope(ODataEntry entry, bool skipWriting, bool writingResponse, ODataWriterBehavior writerBehavior)
                : base(entry, skipWriting, writingResponse, writerBehavior)
            {
                DebugUtils.CheckNoExternalCallers();
            }

            /// <summary>
            /// Marks the <paramref name="atomElement"/> as written in this entry scope.
            /// </summary>
            /// <param name="atomElement">The ATOM element which was written.</param>
            internal void SetWrittenElement(AtomElement atomElement)
            {
                DebugUtils.CheckNoExternalCallers();
                Debug.Assert(!this.IsElementWritten(atomElement), "Can't write the same element twice.");
                this.alreadyWrittenElements |= (int)atomElement;
            }

            /// <summary>
            /// Determines if the <paramref name="atomElement"/> was already written for this entry scope.
            /// </summary>
            /// <param name="atomElement">The ATOM element to test for.</param>
            /// <returns>true if the <paramref name="atomElement"/> was already written for this entry scope; false otherwise.</returns>
            internal bool IsElementWritten(AtomElement atomElement)
            {
                DebugUtils.CheckNoExternalCallers();
                return (this.alreadyWrittenElements & (int)atomElement) == (int)atomElement;
            }
        }
    }
}
