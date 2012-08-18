/*
* DesignerSerializer.cs - serializes the changes made in the designer to ASP.NET code
*					and inserts the changes in the source code editor
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
using System.Threading;
using System.Collections;
using System.ComponentModel;
using System.ComponentModel.Design;

using MonoDevelop.Xml.StateEngine;
using MonoDevelop.AspNet.Parser;
using ICSharpCode.NRefactory;

using AspNetEdit.Tools;
using ICSharpCode.NRefactory.TypeSystem;

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

		public void RemoveControlTag (string id)
		{
			if (id == null)
				throw new ArgumentNullException ("Cannot find component by an empty string");

			IComponent comp = host.GetComponent (id);
			if (comp == null)
				throw new InvalidOperationException ("Component with that name doesn't exists: " + id);

			XDocument doc = document.Parse ().XDocument;
			XElement tag = GetControlTag (doc.RootElement, id);
			if (tag == null)
				throw new InvalidOperationException ("The the tag for the component was not found. ID: " + id);

			DomRegion region;
			if (tag.IsSelfClosing)
				region = tag.Region;
			else if (tag.IsClosed)
				region = new DomRegion (tag.Region.Begin, tag.ClosingTag.Region.End);
			else
				throw new InvalidOperationException ("The tag has no closing tag. It cannot be removed");

			document.RemoveText (region);

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

