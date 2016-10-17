namespace Leo.FileTypeSupport.MXLIFF
{
    using Sdl.Core.Globalization;
    using Sdl.Core.Settings;
    using Sdl.FileTypeSupport.Framework.BilingualApi;
    using Sdl.FileTypeSupport.Framework.Core.Utilities.NativeApi;
    using Sdl.FileTypeSupport.Framework.IntegrationApi;
    using Sdl.FileTypeSupport.Framework.NativeApi;
    using System;
    using System.Collections.Generic;
    using System.Globalization;
    using System.Text.RegularExpressions;
    using System.Xml;

    internal class MXLIFFParser : AbstractBilingualFileTypeComponent, IBilingualParser, INativeContentCycleAware, ISettingsAware
    {
        private readonly Dictionary<int, string> users = new Dictionary<int, string>();
        private XmlDocument document;
        private IDocumentProperties documentProperties;
        private IFileProperties fileProperties;
        private XmlNamespaceManager nsmgr;
        private int srcSegmentTagCount;
        private int tmpTotalTagCount;
        private int totalTagCount;
        private int workflowLevel = 0;

        public event EventHandler<ProgressEventArgs> Progress;

        public IDocumentProperties DocumentProperties
        {
            get
            {
                return documentProperties;
            }
            set
            {
                documentProperties = value;
            }
        }

        public IBilingualContentHandler Output
        {
            get;
            set;
        }

        public void Dispose()
        {
            document = null;
        }

        public void EndOfInput()
        {
            OnProgress(100);
            document = null;
        }

        public void InitializeSettings(ISettingsBundle settingsBundle, string configurationId)
        {
            // Loading of filter settings
        }

        public bool ParseNext()
        {
            if (documentProperties == null)
            {
                documentProperties = ItemFactory.CreateDocumentProperties();
            }

            Output.Initialize(documentProperties);

            IFileProperties fileInfo = ItemFactory.CreateFileProperties();
            fileInfo.FileConversionProperties = fileProperties.FileConversionProperties;
            Output.SetFileProperties(fileInfo);

            // Variables for the progress report
            var xmlNodeList = document.SelectNodes("//x:group", nsmgr);
            if (xmlNodeList != null)
            {
                int totalUnitCount = xmlNodeList.Count;
                int currentUnitCount = 0;
                foreach (XmlNode item in xmlNodeList)
                {
                    Output.ProcessParagraphUnit(CreateParagraphUnit(item));

                    // Update the progress report
                    currentUnitCount++;
                    OnProgress(Convert.ToByte(Math.Round(100 * ((decimal)currentUnitCount / totalUnitCount), 0)));
                }
            }

            Output.FileComplete();
            Output.Complete();

            return false;
        }

        public void SetFileProperties(IFileProperties properties)
        {
            fileProperties = properties;
        }

        public void StartOfInput()
        {
            OnProgress(0);
            document = new XmlDocument();
            document.Load(fileProperties.FileConversionProperties.OriginalFilePath);
            nsmgr = new XmlNamespaceManager(document.NameTable);
            nsmgr.AddNamespace("x", "urn:oasis:names:tc:xliff:document:1.2");
            nsmgr.AddNamespace("m", "http://www.memsource.com/mxlf/2.0");

            // Acquire workflow level
            var level = document.DocumentElement.Attributes["m:level"];

            if (level != null)
            {
                workflowLevel = Int32.Parse(level.Value);
            }

            // Acquire users
            var memsourceUsers = document.SelectNodes("//m:user", nsmgr);

            if (memsourceUsers != null)
            {
                foreach (XmlElement user in memsourceUsers)
                {
                    var id = user.Attributes["id"]?.Value;
                    var username = user.Attributes["username"]?.Value;

                    users.Add((id != null ? Convert.ToInt32(id) : 0), username);
                }
            }
        }

        protected virtual void OnProgress(byte percent)
        {
            Progress?.Invoke(this, new ProgressEventArgs(percent));
        }

        private ICommentProperties CreateComment(XmlNode commentNode)
        {
            ICommentProperties commentProperties = PropertiesFactory.CreateCommentProperties();

            var createdBy = commentNode.Attributes["created-by"]?.Value;
            var resolved = commentNode.Attributes["resolved"]?.Value;

            string author = string.Empty;
            if (createdBy != null)
            {
                author = users[Convert.ToInt32(createdBy)];
            }

            Severity severity = Severity.Low;

            if (resolved != null && (resolved == "false"))
            {
                severity = Severity.Medium;
            }

            var comment = PropertiesFactory.CreateComment(commentNode.InnerText,
                                                          author, severity);

            commentProperties.Add(comment);

            return commentProperties;
        }

        private ConfirmationLevel CreateConfirmationLevel(XmlNode transUnit)
        {
            ConfirmationLevel sdlxliffLevel;

            if (transUnit != null)
            {
                var confirmed = transUnit.Attributes["m:confirmed"];
                var levelEdited = transUnit.Attributes["m:level-edited"];

                if (confirmed != null && (confirmed.Value == "1"))
                {
                    if (workflowLevel > 1 && levelEdited != null && (levelEdited.Value == "true"))
                    {
                        sdlxliffLevel = ConfirmationLevel.ApprovedTranslation;
                    }
                    else
                    {
                        sdlxliffLevel = ConfirmationLevel.Translated;
                    }
                }
                else
                {
                    if (workflowLevel > 1 && levelEdited != null && (levelEdited.Value == "true"))
                    {
                        sdlxliffLevel = ConfirmationLevel.RejectedTranslation;
                    }
                    else
                    {
                        sdlxliffLevel = ConfirmationLevel.Draft;
                    }
                }
            }
            else
            {
                sdlxliffLevel = ConfirmationLevel.Unspecified;
            }

            return sdlxliffLevel;
        }

        private IContextProperties CreateContext(string id, string paraId)
        {
            IContextProperties contextProperties = PropertiesFactory.CreateContextProperties();

            IContextInfo contextId = PropertiesFactory.CreateContextInfo("id");
            contextId.SetMetaData("ID", id);
            contextId.Description = "Trans-unit id";
            contextId.DisplayCode = "ID";

            IContextInfo contextParaId = PropertiesFactory.CreateContextInfo("para-id");
            contextParaId.SetMetaData("PARA ID", paraId);
            contextParaId.Description = "Para-id";
            contextParaId.DisplayCode = "PARA ID";

            contextProperties.Contexts.Add(contextId);
            contextProperties.Contexts.Add(contextParaId);

            return contextProperties;
        }

        private Byte CreateMatchValue(XmlNode transUnit)
        {
            Byte matchValue = 0;

            // "m:gross-score" and "m:score" are both percentage values of TM matches from which the given segment is pre-translated.
            // "m:gross-score" is the percentage prior to the application of TM penalization, if any, while "m:score" is the percentage after penalization.
            var score = transUnit.Attributes["m:score"];

            if (score != null)
            {
                matchValue = Convert.ToByte(Convert.ToDouble(score.Value) * 100);
            }

            return matchValue;
        }

        private IParagraphUnit CreateParagraphUnit(XmlNode xmlUnit)
        {
            // Create paragraph unit object
            IParagraphUnit paragraphUnit = ItemFactory.CreateParagraphUnit(LockTypeFlags.Unlocked);

            var xmlNode = xmlUnit.SelectSingleNode("x:trans-unit", nsmgr);

            if (xmlNode != null)
            {
                var id = xmlNode.Attributes["id"];
                var paraId = xmlNode.Attributes["m:para-id"];

                if (id != null && paraId != null)
                {
                    paragraphUnit.Properties.Contexts = CreateContext(id.Value, paraId.Value);
                }

                // Create segment pair object
                ISegmentPairProperties segmentPairProperties = ItemFactory.CreateSegmentPairProperties();
                ITranslationOrigin tuOrg = ItemFactory.CreateTranslationOrigin();

                // Assign the appropriate confirmation level to the segment pair
                segmentPairProperties.ConfirmationLevel = CreateConfirmationLevel(xmlNode);
                tuOrg.MatchPercent = CreateMatchValue(xmlNode);

                // Add source segment to paragraph unit
                ISegment srcSegment = CreateSegment(xmlNode.SelectSingleNode("x:source", nsmgr), segmentPairProperties, true);
                paragraphUnit.Source.Add(srcSegment);

                // Add target segment to paragraph unit if available
                if (xmlNode.SelectSingleNode("x:target", nsmgr) != null)
                {
                    ISegment trgSegment = CreateSegment(xmlNode.SelectSingleNode("x:target", nsmgr), segmentPairProperties, false);

                    // Check if locked
                    var locked = xmlNode.Attributes["m:locked"];
                    if (locked != null && locked.Value == "true")
                    {
                        trgSegment.Properties.IsLocked = true;
                    }

                    // Check if target empty and look for alt-trans
                    if (trgSegment.Count == 0)
                    {
                        var alttrans = xmlNode.SelectSingleNode("x:alt-trans/x:target", nsmgr);
                        if (alttrans != null && !string.IsNullOrWhiteSpace(alttrans.InnerText))
                        {
                            PopulateSegment(trgSegment, alttrans, false);

                            var alttransOrigin = xmlNode.SelectSingleNode("x:alt-trans", nsmgr).Attributes["origin"];
                            if (alttransOrigin != null)
                            {
                                var origin = alttransOrigin.Value;
                                if (origin.Contains("machine") || origin.Contains("mt"))
                                {
                                    tuOrg.OriginType = DefaultTranslationOrigin.MachineTranslation;
                                }
                                else if (origin.Contains("tm"))
                                {
                                    tuOrg.OriginType = DefaultTranslationOrigin.TranslationMemory;
                                }

                                tuOrg.OriginSystem = origin;
                            }
                        }
                    }

                    paragraphUnit.Target.Add(trgSegment);
                }
                else
                {
                    var singleNode = xmlNode.SelectSingleNode("x:source", nsmgr);
                    if (singleNode != null) singleNode.InnerText = "";
                    ISegment trgSegment = CreateSegment(xmlNode.SelectSingleNode("x:source", nsmgr), segmentPairProperties, false);
                    paragraphUnit.Target.Add(trgSegment);
                }

                var transOrigin = xmlNode.Attributes["m:trans-origin"];
                if (transOrigin.Value != null && transOrigin.Value != "null")
                {
                    tuOrg.OriginType = transOrigin.Value;
                }

                segmentPairProperties.TranslationOrigin = tuOrg;

                // Add comments
                if (xmlNode.SelectSingleNode("m:comment", nsmgr) != null)
                {
                    paragraphUnit.Properties.Comments = CreateComment(xmlNode.SelectSingleNode("m:comment", nsmgr));
                }
            }

            return paragraphUnit;
        }

        private IPlaceholderTag CreatePhTag(string tagContent, int tagNo, bool source)
        {
            IPlaceholderTagProperties phTagProperties = PropertiesFactory.CreatePlaceholderTagProperties(tagContent);
            IPlaceholderTag phTag = ItemFactory.CreatePlaceholderTag(phTagProperties);

            phTagProperties.TagContent = tagContent;
            phTagProperties.DisplayText = string.Format("{{{0}}}", tagNo);
            phTagProperties.CanHide = false;

            if (source)
            {
                var thisId =
                    new TagId(totalTagCount.ToString(CultureInfo.InvariantCulture));

                phTagProperties.TagId = thisId;
                totalTagCount += 1;
                tmpTotalTagCount += 1;
                srcSegmentTagCount += 1;
            }
            else
            {
                var thisId =
                    new TagId(totalTagCount.ToString(CultureInfo.InvariantCulture));

                phTagProperties.TagId = thisId;
                totalTagCount += 1;
            }

            return phTag;
        }

        private ISegment CreateSegment(XmlNode segNode, ISegmentPairProperties pair, bool source)
        {
            ISegment segment = ItemFactory.CreateSegment(pair);

            if (source)
            {
                srcSegmentTagCount = 0;
                if (totalTagCount < tmpTotalTagCount)
                {
                    totalTagCount = tmpTotalTagCount;
                }
            }
            else
            {
                totalTagCount = totalTagCount - srcSegmentTagCount;
            }

            PopulateSegment(segment, segNode, source);

            return segment;
        }

        private IText CreateText(string segText)
        {
            ITextProperties textProperties = PropertiesFactory.CreateTextProperties(segText);
            IText textContent = ItemFactory.CreateText(textProperties);

            return textContent;
        }

        private void PopulateSegment(ISegment segment, XmlNode node, bool source)
        {
            int i = 1;
            foreach (XmlNode item in node.ChildNodes)
            {
                if (item.NodeType == XmlNodeType.Text)
                {
                    foreach (var chunk in Regex.Split(item.InnerText, "((?:{|<)[a-z0-9]{1,2}(?:>|}))"))
                    {
                        if (Regex.IsMatch(chunk, "((?:{|<)[a-z0-9]{1,2}(?:>|}))"))
                        {
                            segment.Add(CreatePhTag(chunk, i, source));
                            i++;
                        }
                        else
                        {
                            if (!string.IsNullOrEmpty(chunk))
                            {
                                segment.Add(CreateText(chunk));
                            }
                        }
                    }
                }

                if (item.NodeType == XmlNodeType.Element)
                {
                    segment.Add(CreatePhTag(item.OuterXml, i, source));
                    i++;
                }
            }
        }
    }
}