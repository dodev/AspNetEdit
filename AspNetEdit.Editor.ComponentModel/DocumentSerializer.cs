/* 
* DocumentSerializer.cs - Generates HTML needed for
*						the designer surface
*
* Authors: 
*  Petar Dodev <petar.dodev@gmail.com>
*
* Copyright (C) 2012 Petar Dodev
*
* Licensed under the Apache License, Version 2.0 (the "License");
* you may not use this file except in compliance with the License.
* You may obtain a copy of the License at
*
*	http://www.apache.org/licenses/LICENSE-2.0
*
* Unless required by applicable law or agreed to in writing, software
* distributed under the License is distributed on an "AS IS" BASIS,
* WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied.
* See the License for the specific language governing permissions and
* limitations under the License.
*/

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
using AspNetEdit.Editor.DesignerLink;

namespace AspNetEdit.Editor.ComponentModel
{
	public class DocumentSerializer
	{
		DesignerHost host;
		Document document;

		/// <summary>
		/// The designer context.
		/// </summary>
		/// <description>
		/// the designer context is a string of <link> and <script> tags that link to
		/// javascript and css files in the instalation directory of the addin.
		/// They make the WebKit.WebView act as a designer surface, which sends
		/// messages to the C# backend and has specific stiles for the design-time
		/// components.
		/// </description>
		string designerContext;

		public DocumentSerializer (DesignerHost hst)
		{
			host = hst;
			document = host.RootDocument;
			designerContext = String.Empty;
			//designerContext = ((host.GetDesigner (host.RootComponent) as RootDesigner).GetView (ViewTechnology.Default) as RootDesignerView).DesignerContext;
		}

		/// <summary>
		/// Sets the designer context.
		/// </summary>
		/// <param name='desCtx'>
		/// The designer context string.
		/// </param>
		public void SetDesignerContext (string desCtx)
		{
			designerContext = desCtx;
		}

		/// <summary>
		/// Gets the designable html.
		/// </summary>
		/// <description>
		/// Generates HTML string to be displayed in the RootDesignerView's WebView.
		/// It contains the designer context in the <head> tag and the init params
		/// in the beginning of the <body> tag.
		/// Also all the ASP.NET and HTML controls are rendered with their HTML representations
		/// to be displayed the way they're going to look to the user.
		/// </description>
		/// <returns>
		/// The designable html string.
		/// </returns>
		public string GetDesignableHtml ()
		{
			var parsedDoc = document.Parse () as AspNetParsedDocument;
			StringBuilder sb = new StringBuilder ();

			SerializeNode (parsedDoc.XDocument.RootElement, sb);
			return sb.ToString ();
		}

		// used to track where is the end of the previous tag
		TextLocation prevTagLocation = TextLocation.Empty;

		/// <summary>
		/// Serializes a XNode to a HTML tag. This is a recursive method.
		/// </summary>
		/// <param name='node'>
		/// Node.
		/// </param>
		/// <param name='sb'>
		/// A string builder instance.
		/// </param>
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
			strBuilder.AppendFormat ("<div id=\"{0}\" style=\"display:none;\"> ", DesignerNames.ElementInitContClass);
			strBuilder.AppendFormat ("<span id=\"{0}\">", DesignerNames.ElementSelectableContClass);
			jsonizer.Serialize (clientIds.ToArray (), strBuilder);
			strBuilder.Append ("</span>");
			strBuilder.AppendFormat ("<span id=\"{0}\">", DesignerNames.ElementSeletectedContClass);
			jsonizer.Serialize (selectedIds.ToArray (), strBuilder);
			strBuilder.Append ("</span>");
			strBuilder.Append ("</div>");
		}
	}
}

