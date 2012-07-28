// 
// DocumentSerializer.cs - Generates HTML needed for
//						the designer surface
//  
// Author:
//       Petar Dodev <petar.dodev@gmail.com>
// 
// Copyright (c) 2012 Petar Dodev
// 
// Permission is hereby granted, free of charge, to any person obtaining a copy
// of this software and associated documentation files (the "Software"), to deal
// in the Software without restriction, including without limitation the rights
// to use, copy, modify, merge, publish, distribute, sublicense, and/or sell
// copies of the Software, and to permit persons to whom the Software is
// furnished to do so, subject to the following conditions:
// 
// The above copyright notice and this permission notice shall be included in
// all copies or substantial portions of the Software.
// 
// THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
// IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
// FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
// AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
// LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM,
// OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN
// THE SOFTWARE.
using System;
using System.IO;
using System.Web.UI;
using System.ComponentModel;
using System.ComponentModel.Design;
using System.Text;
using System.Collections;
using System.Collections.Generic;

using MonoDevelop.Xml.StateEngine;
using MonoDevelop.AspNet.Parser;
using ICSharpCode.NRefactory;
using MonoDevelop.SourceEditor;

using AspNetEdit.Editor.UI;
using AspNetEdit.Tools;

namespace AspNetEdit.Editor.ComponentModel
{
	public class DocumentSerializer
	{
		DesignerHost host;
		Document document;
		string designerContext;

		public DocumentSerializer (DesignerHost hst)
		{
			host = hst;
			document = host.RootDocument;
			designerContext = ((host.GetDesigner (host.RootComponent) as RootDesigner).GetView (ViewTechnology.Default) as RootDesignerView).DesignerContext;
		}

		public string GetDesignableHtml ()
		{
			var parsedDoc = document.Parse () as AspNetParsedDocument;
			StringBuilder sb = new StringBuilder ();

			SerializeNode (parsedDoc.XDocument.RootElement, sb);
			return sb.ToString ();
		}

		TextLocation prevTagLocation = TextLocation.Empty;

		void SerializeNode (XNode node, StringBuilder sb)
		{
			prevTagLocation = node.Region.End;

			var element = node as XElement;
			if (element == null)
				return;

			string id = XDocumentHelper.GetAttributeValueCI (element.Attributes, "id");

			// Controls are runat="server" and have unique id in the Container
			if (element.Name.HasPrefix || XDocumentHelper.IsRunAtServer (element)) {
				IComponent component = host.GetComponent (id);

				// HTML controls, doesn't need special rendering
				var control = component as Control;

				// genarete placeholder
				if (control != null) {
					StringWriter strWriter = new StringWriter ();
					HtmlTextWriter writer = new HtmlTextWriter (strWriter);
					control.RenderControl (writer);
					writer.Close ();
					strWriter.Flush ();
					sb.Append (strWriter.ToString ());
					strWriter.Close ();
					if (!element.IsSelfClosing)
						prevTagLocation = element.ClosingTag.Region.End;
					return;
				}
			}

			// strip script tags
			if (element.Name.Name.ToLower () == "script")
				return;

			// the node is a html element
			sb.AppendFormat ("<{0}", element.Name.FullName);
			
			// print the attributes
			foreach (MonoDevelop.Xml.StateEngine.XAttribute attr in element.Attributes) {
				string name = attr.Name.Name.ToLower ();
				// strip runat and on* event attributes
				if ((name != "runat") && (name.Substring (0, 2).ToLower () != "on"))
					sb.AppendFormat (" {0}=\"{1}\"", attr.Name.FullName, attr.Value);
			}
			
			if (element.IsSelfClosing) {
				sb.Append (" />");
			} else {
				sb.Append (">");

				// we are currentyl on the head tag
				// add designer content - js and css
				if (element.Name.Name.ToLower () == "head") {
					sb.Append (designerContext);
				}

				if (element.Name.Name.ToLower () == "body") {
					GetDesignerInitParams (sb);
				}

				// serializing the childnodes if any
				foreach (MonoDevelop.Xml.StateEngine.XNode nd in element.Nodes) {
					// get the text before the openning tag of the child element
					sb.Append (document.GetTextFromEditor (prevTagLocation, nd.Region.Begin));
					// and the element itself
					SerializeNode (nd, sb);
				}
				
				// printing the text after the closing tag of the child elements
				int lastChildEndLine = element.Region.EndLine;
				int lastChildEndColumn = element.Region.EndColumn;
				
				// if the element have 1+ children
				if (element.LastChild != null) {
					var lastChild = element.LastChild as MonoDevelop.Xml.StateEngine.XElement;
					// the last child is an XML tag
					if (lastChild != null) {
						// the tag is selfclosing
						if (lastChild.IsSelfClosing) {
							lastChildEndLine = lastChild.Region.EndLine;
							lastChildEndColumn = lastChild.Region.EndColumn;
							// the tag is not selfclosing and has a closing tag
						} else if (lastChild.ClosingTag != null) {
							lastChildEndLine = lastChild.ClosingTag.Region.EndLine;
							lastChildEndColumn = lastChild.ClosingTag.Region.EndColumn;
						} else {
							// TODO: the element is not closed. Warn the user
						}
						// the last child is not a XML element. Probably AspNet tag. TODO: find the end location of that tag
					} else {
						lastChildEndLine = element.LastChild.Region.EndLine;
						lastChildEndLine = element.LastChild.Region.EndLine;
					}
				}
				
				if (element.ClosingTag != null) {
					sb.Append (document.GetTextFromEditor (new TextLocation (lastChildEndLine, lastChildEndColumn), element.ClosingTag.Region.Begin));
					prevTagLocation = element.ClosingTag.Region.End;
				} else {
					// TODO: the element is not closed. Warn the user
				}
				
				sb.AppendFormat ("</{0}>", element.Name.FullName);
			}
		}

		/// <summary>
		/// Gets the designer surface init parameters
		/// For now they're used for preserving selected items between reloads
		/// </summary>
		void GetDesignerInitParams (StringBuilder strBuilder)
		{
			List<string> clientIds = new List<string> (host.Container.Components.Count);
			foreach (IComponent comp in host.Container.Components) {
				clientIds.Add ((comp as Control).ClientID);
			}

			var selServ = host.GetService (typeof (ISelectionService)) as ISelectionService;
			if (selServ == null)
				throw new Exception ("Could not load selection service");
			ICollection col = selServ.GetSelectedComponents ();
			List<string> selectedIds = new List<string> (col.Count);
			foreach (IComponent comp in col) {
				selectedIds.Add ((comp as Control).ClientID);
			}

			System.Web.Script.Serialization.JavaScriptSerializer jsonizer = new System.Web.Script.Serialization.JavaScriptSerializer ();
			strBuilder.Append ("<div id=\"aspnetedit_init_values_container\" style=\"display:none;\"> ");
			strBuilder.Append ("<span id=\"aspnetedit_selectable_items\">");
			jsonizer.Serialize (clientIds.ToArray (), strBuilder);
			strBuilder.Append ("</span>");
			strBuilder.Append ("<span id=\"aspnetedit_selected_items\">");
			jsonizer.Serialize (selectedIds.ToArray (), strBuilder);
			strBuilder.Append ("</span>");
			strBuilder.Append ("</div>");
		}
	}
}

