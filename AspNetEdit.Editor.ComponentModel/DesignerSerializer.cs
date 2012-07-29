// 
// DesignerSerializer.cs - serializes the changes made in the designer to ASP.NET code
//					and inserts the changes in the source code editor
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
using System.ComponentModel;

using MonoDevelop.Xml.StateEngine;
using MonoDevelop.AspNet.Parser;
using ICSharpCode.NRefactory;

using AspNetEdit.Tools;

namespace AspNetEdit.Editor.ComponentModel
{
	public class DesignerSerializer
	{
		DesignerHost host;
		Document document;

		public DesignerSerializer (DesignerHost hst)
		{
			host = hst;
			document = host.RootDocument;
		}

		// adds an attribute to the end of the openning  tag
		public void InsertAttribute (XElement el, string key, string value)
		{
			int line = el.Region.EndLine;
			int column = 1;
			string preambula = String.Empty;
			string ending = String.Empty;

			if (el.IsSelfClosing) {
				column = el.Region.EndColumn - 2; // "/>"
				ending = " ";
			} else {
				column = el.Region.EndColumn -1; // ">"
			}

			if (column > 1) {
				string whatsBeforeUs = document.GetTextFromEditor (line, column - 1, line, column);
				if (!String.IsNullOrWhiteSpace (whatsBeforeUs))
					preambula = " ";
			}

			document.InsertText (
				new TextLocation (line, column),
				String.Format ("{0}{1}=\"{2}\"{3}", preambula, key, value, ending)
			);
		}

		public void UpdateAttribute (XElement el, string key, string value)
		{
			UpdateAttribute (
				XDocumentHelper.GetAttributeCI (el.Attributes, key),
				value
			);
		}

		public void UpdateAttribute (XAttribute attr, string newValue)
		{
			document.ReplaceText (
				attr.Region, 
			    String.Format ("{0}=\"{1}\"", attr.Name.Name, newValue)
			);
		}

		public void SetAttribtue (XElement el, string key, string value)
		{
			XAttribute attr = XDocumentHelper.GetAttributeCI (el.Attributes, key);
			if (attr == null)
				InsertAttribute (el, key, value);
			else if (attr.Value != value)
				UpdateAttribute (attr, value);
		}

		public void RemoveAttribute (XElement el, string key)
		{
			document.RemoveText (
				XDocumentHelper.GetAttributeCI (el.Attributes, key).Region
			);
		}

		public void UpdateTag (IComponent component, MemberDescriptor memberDesc, object newVal)
		{
			string key = String.Empty;
			string value = String.Empty;
			bool removeOnly = false;
			AspNetParsedDocument doc = host.RootDocument.Parse ();
			XElement el = GetControlTag (doc.XDocument.RootElement, component.Site.Name);

			if (memberDesc is PropertyDescriptor) {
				var propDesc = memberDesc as PropertyDescriptor;
				// check if the value is the default for the property of the component
				// remove the attribute if it's the default
				if (propDesc.Attributes.Contains (new DefaultValueAttribute (newVal))) {
					removeOnly = true;
				} else {
					key = memberDesc.Name;
					value = propDesc.Converter.ConvertToString (newVal);
				}
			} else if (memberDesc is EventDescriptor) {
				//var eventDesc = memberDesc as EventDescriptor;
				//key = "On" + eventDesc.Name;
				// TODO: get the handler method name
				//value = newVal.ToString ();
			} else {
				// well, well, well! what do we have here!
			}

			if (removeOnly)
				RemoveAttribute (el, memberDesc.Name);
			else
				SetAttribtue (el, key, value);
		}

		XElement GetControlTag (XElement container, string id)
		{
			XElement controlTag = null;
			foreach (XNode node in container.Nodes) {
				if (controlTag != null) {
					break;
				}
				if (node is XElement) {
					XElement el = node as XElement;
					string currId = XDocumentHelper.GetAttributeValueCI (el.Attributes, "id");
					if (XDocumentHelper.IsRunAtServer (el) && (string.Compare(currId, id, true) == 0)) {
						controlTag = el;
						break;
					}
					controlTag = GetControlTag (el, id);
				} 
			}
			return controlTag;
		}
	}
}

