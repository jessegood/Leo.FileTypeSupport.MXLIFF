using Sdl.Core.Globalization;
using Sdl.FileTypeSupport.Framework.BilingualApi;
using Sdl.FileTypeSupport.Framework.NativeApi;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Xml;

namespace Leo.FileTypeSupport.MXLIFF
{
    internal class MXLIFFWriter : AbstractBilingualFileTypeComponent, IBilingualWriter, INativeOutputSettingsAware
    {
        private INativeOutputFileProperties nativeFileProperties;
        private XmlNamespaceManager nsmgr;
        private IPersistentFileConversionProperties originalFileProperties;
        private XmlDocument targetFile;
        private MXLIFFTextExtractor textExtractor;
        private int workflowLevel = 0;
        private Dictionary<string, int> users = new Dictionary<string, int>();

        public void Complete()
        {
        }

        public void Dispose()
        {
            // Don't need to dispose of anything
        }

        public void FileComplete()
        {
            using (XmlTextWriter wr = new XmlTextWriter(nativeFileProperties.OutputFilePath, Encoding.UTF8))
            {
                wr.Formatting = Formatting.None;
                targetFile.Save(wr);
                targetFile = null;
            }
        }

        public void GetProposedOutputFileInfo(IPersistentFileConversionProperties fileProperties, IOutputFileInfo proposedFileInfo)
        {
            originalFileProperties = fileProperties;
        }

        public void Initialize(IDocumentProperties documentInfo)
        {
            textExtractor = new MXLIFFTextExtractor();
        }

        public void ProcessParagraphUnit(IParagraphUnit paragraphUnit)
        {
            string id = paragraphUnit.Properties.Contexts.Contexts[0].GetMetaData("ID");
            XmlNode xmlUnit = targetFile.SelectSingleNode("//x:trans-unit[@id='" + id + "']", nsmgr);

            CreateParagraphUnit(paragraphUnit, xmlUnit);
        }

        public void SetFileProperties(IFileProperties fileInfo)
        {
            targetFile = new XmlDocument();
            targetFile.PreserveWhitespace = false;
            targetFile.Load(originalFileProperties.OriginalFilePath);
            nsmgr = new XmlNamespaceManager(targetFile.NameTable);
            nsmgr.AddNamespace("x", "urn:oasis:names:tc:xliff:document:1.2");
            nsmgr.AddNamespace("m", "http://www.memsource.com/mxlf/2.0");

            var level = targetFile.DocumentElement.Attributes["m:level"];

            if (level != null)
            {
                workflowLevel = Int32.Parse(level.Value);
            }

            // Acquire users
            var memsourceUsers = targetFile.SelectNodes("//m:user", nsmgr);

            if (memsourceUsers != null)
            {
                foreach (XmlElement user in memsourceUsers)
                {
                    var id = user.Attributes["id"]?.Value;
                    var username = user.Attributes["username"]?.Value;

                    users.Add(username, id != null ? Convert.ToInt32(id) : 0);
                }
            }
        }

        public void SetOutputProperties(INativeOutputFileProperties properties)
        {
            nativeFileProperties = properties;
        }

        private void AddComments(XmlNode xmlUnit, List<IComment> comments)
        {
            var text = string.Empty;
            var comment = comments.First();

            // We concatenate all comment text if there are multiple
            foreach (var c in comments)
            {
                text += c.Text + " ";
            }

            var createdat = targetFile.CreateAttribute("created-at");
            var createdby = targetFile.CreateAttribute("created-by");
            var modifiedat = targetFile.CreateAttribute("modified-at");
            modifiedat.Value = "0";

            var modifiedby = targetFile.CreateAttribute("modified-by");

            var resolved = targetFile.CreateAttribute("resolved");
            resolved.Value = "false";

            string commentDate = this.GetCommentDate();

            // Convert DateTime to Unix timestamp in milliseconds
            long milliseconds = (long)(TimeZoneInfo.ConvertTimeToUtc(comment.Date) - new DateTime(1970, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc)).TotalMilliseconds;
            createdat.Value = milliseconds.ToString();

            // Try to find the user id by author name
            // If it fails, just leave it blank
            if (users.ContainsKey(comment.Author))
            {
                createdby.Value = users[comment.Author].ToString();
            }
            else
            {
                createdby.Value = "";
            }

            xmlUnit.Attributes.Append(createdat);
            xmlUnit.Attributes.Append(createdby);
            xmlUnit.Attributes.Append(modifiedat);
            xmlUnit.Attributes.Append(modifiedby);
            xmlUnit.Attributes.Append(resolved);

            xmlUnit.InnerText = text;
        }

        private void CreateParagraphUnit(IParagraphUnit paragraphUnit, XmlNode transUnit)
        {
            // Iterate all segment pairs
            foreach (ISegmentPair segmentPair in paragraphUnit.SegmentPairs)
            {
                Byte matchPercent = 0;

                if (transUnit != null)
                {
                    XmlNode source = transUnit.SelectSingleNode("x:source", nsmgr);

                    if (source != null)
                    {
                        source.InnerText = textExtractor.GetPlainText(segmentPair.Source);
                    }

                    if (segmentPair.Properties.TranslationOrigin != null)
                    {
                        matchPercent = segmentPair.Properties.TranslationOrigin.MatchPercent;
                    }
                }

                XmlNode target;
                if (transUnit.SelectSingleNode("x:target", nsmgr) == null)
                {
                    XmlDocument segDoc = new XmlDocument();
                    string nodeContent = transUnit.OuterXml;
                    segDoc.LoadXml(nodeContent);
                    XmlNode trgNode = segDoc.CreateNode(XmlNodeType.Element, "target", nsmgr.DefaultNamespace);

                    XmlNode importNode = transUnit.OwnerDocument.ImportNode(trgNode, true);
                    transUnit.AppendChild(importNode);
                    transUnit.SelectSingleNode("x:target", nsmgr).InnerXml =
                        textExtractor.GetPlainText(segmentPair.Target);
                }
                else
                {
                    target = transUnit.SelectSingleNode("x:target", nsmgr);
                    target.InnerText = textExtractor.GetPlainText(segmentPair.Target);
                }

                // Add comments (if applicable)
                var comments = textExtractor.GetSegmentComment(segmentPair.Target);
                if (comments.Count > 0 && transUnit.SelectSingleNode("m:comment", nsmgr) == null)
                {
                    XmlElement commentElement = targetFile.CreateElement("m:comment", nsmgr.LookupNamespace("m"));

                    var tunitMetaData = transUnit.SelectSingleNode("m:tunit-metadata", nsmgr);
                    if (tunitMetaData != null)
                    {
                        transUnit.InsertBefore(commentElement, tunitMetaData);
                        AddComments(transUnit.SelectSingleNode("m:comment", nsmgr), comments);
                    }
                }

                // Update score value
                var dbl = matchPercent / 100.0;
                if (transUnit.Attributes["m:score"] != null)
                {
                    transUnit.Attributes["m:score"].Value = dbl.ToString();
                }
                else
                {
                    transUnit.Attributes.Append(transUnit.OwnerDocument.CreateAttribute("m:score"));
                    transUnit.Attributes["m:score"].Value = dbl.ToString();
                }

                // Update m:locked
                if (transUnit.Attributes["m:locked"] != null)
                {
                    var isLocked = segmentPair.Target?.Properties?.IsLocked.ToString();
                    transUnit.Attributes["m:locked"].Value = isLocked != null ? isLocked.ToLower() : "false";
                }

                // Update m:confirmed
                if (transUnit.Attributes["m:confirmed"] != null)
                {
                    if (segmentPair.Target != null && segmentPair.Target.Properties != null)
                    {
                        switch (segmentPair.Target.Properties.ConfirmationLevel)
                        {
                            case ConfirmationLevel.Unspecified:
                                transUnit.Attributes["m:confirmed"].Value = "0";
                                break;

                            case ConfirmationLevel.Draft:
                                transUnit.Attributes["m:confirmed"].Value = "0";
                                break;

                            case ConfirmationLevel.Translated:
                                transUnit.Attributes["m:confirmed"].Value = "1";
                                break;

                            case ConfirmationLevel.RejectedTranslation:
                                transUnit.Attributes["m:confirmed"].Value = "0";
                                break;

                            case ConfirmationLevel.ApprovedTranslation:
                                transUnit.Attributes["m:confirmed"].Value = "1";
                                break;

                            case ConfirmationLevel.RejectedSignOff:
                                transUnit.Attributes["m:confirmed"].Value = "0";
                                break;

                            case ConfirmationLevel.ApprovedSignOff:
                                transUnit.Attributes["m:confirmed"].Value = "0";
                                break;

                            default:
                                break;
                        }
                    }
                }
            }
        }

        private string GetCommentDate()
        {
            string day;
            string month;

            if (DateTime.UtcNow.Month.ToString().Length == 1)
                month = "0" + DateTime.UtcNow.Month;
            else
                month = "0" + DateTime.UtcNow.Month;

            if (DateTime.UtcNow.Day.ToString().Length == 1)
                day = "0" + DateTime.UtcNow.Day;
            else
                day = "0" + DateTime.UtcNow.Day;

            return DateTime.UtcNow.Year + month + day + "T" +
                DateTime.UtcNow.Hour + DateTime.UtcNow.Minute +
                DateTime.UtcNow.Second + "Z";
        }
    }
}