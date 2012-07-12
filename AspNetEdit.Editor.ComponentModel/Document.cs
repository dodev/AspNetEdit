/* 
* Document.cs - Represents the DesignerHost's document
* 
* Authors: 
*  Michael Hutchinson <m.j.hutchinson@gmail.com>
*  
* Copyright (C) 2005 Michael Hutchinson
*
* This sourcecode is licenced under The MIT License:
* 
* Permission is hereby granted, free of charge, to any person obtaining
* a copy of this software and associated documentation files (the
* "Software"), to deal in the Software without restriction, including
* without limitation the rights to use, copy, modify, merge, publish,
* distribute, sublicense, and/or sell copies of the Software, and to permit
* persons to whom the Software is furnished to do so, subject to the
* following conditions:
* 
* The above copyright notice and this permission notice shall be included in
* all copies or substantial portions of the Software.
*
* THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS
* OR IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF
* MERCHANTABILITY, FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN
* NO EVENT SHALL THE AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM,
* DAMAGES OR OTHER LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR
* OTHERWISE, ARISING FROM, OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE
* USE OR OTHER DEALINGS IN THE SOFTWARE.
*/

using System;
using System.Text;
using System.Web.UI;
using System.Web.UI.WebControls;
using System.Web.UI.HtmlControls;
using System.IO;
using System.ComponentModel.Design;
using System.Collections;
using System.Collections.Generic;
using System.ComponentModel;
using System.Globalization;
using System.Reflection;

using MonoDevelop.Xml.StateEngine;
using MonoDevelop.AspNet;
using MonoDevelop.AspNet.Parser;
using MonoDevelop.AspNet.StateEngine;
using MonoDevelop.SourceEditor;

using ICSharpCode.NRefactory;
using ICSharpCode.NRefactory.TypeSystem;

namespace AspNetEdit.Editor.ComponentModel
{
	public class Document
	{
		public static readonly string newDocument = "<html>\n<head>\n\t<title>{0}</title>\n</head>\n<body>\n<form runat=\"server\">\n\n</form></body>\n</html>";

		Hashtable directives;

		private Control parent;
		private DesignerHost host;
		
		AspNetParsedDocument aspNetDoc;
		SourceEditorView srcEditor;
		
		///<summary>Creates a new document</summary>
		public Document (Control parent, DesignerHost host, string documentName)
		{
			initDocument (parent, host);
			parse (String.Format (newDocument, documentName), documentName);
		}
		
		///<summary>Creates a document from an existing file</summary>
		public Document (Control parent, DesignerHost host, SourceEditorView srcEditorView, AspNetParsedDocument doc)
		{
			initDocument (parent, host);
			this.srcEditor = srcEditorView;
			this.aspNetDoc = doc;
		}
		
		private void initDocument (Control parent, DesignerHost host)
		{
			System.Diagnostics.Trace.WriteLine ("Creating document...");
			if (!(parent is WebFormPage))
				throw new NotImplementedException ("Only WebFormsPages can have a document for now");
			this.parent = parent;
			this.host = host;
			
			if (!host.Loading)
				throw new InvalidOperationException ("The document cannot be initialised or loaded unless the host is loading"); 

			directives = new Hashtable (StringComparer.InvariantCultureIgnoreCase);
		}

		#region StateEngine parser
		
		void parse (string doc, string fileName)
		{
			var parser = new AspNetParser ();

			using (StringReader strRd = new StringReader (doc)) {
				aspNetDoc = parser.Parse (true, fileName, strRd, srcEditor.Project) as AspNetParsedDocument;
			}
		}
		
		#endregion
		
		#region Designer communication

		public string ToDesignTimeHtml ()
		{
			string doc = null;
			if (aspNetDoc.XDocument != null) {
				doc = serializeNode (aspNetDoc.XDocument.RootElement);
			}

			return doc;
		}
		#endregion
		
		#region Serialization
		
		TextLocation prevTagLocation = new TextLocation (TextLocation.MinLine, TextLocation.MinColumn);
		
		string serializeNode (MonoDevelop.Xml.StateEngine.XNode node)
		{
			prevTagLocation = node.Region.End;
			
			MonoDevelop.Xml.StateEngine.XElement element = node as MonoDevelop.Xml.StateEngine.XElement;
			if (element == null) {
//				switch (node.GetType ().ToString ()) {
//				case typeof (AspNetDirective).ToString ():
//					break;
//				case typeof (AspNetHtmlEncodedExpression).ToString ():
//					break;
//				case typeof (AspNetRenderBlock).ToString ():
//					break;
//				case typeof (AspNetRenderExpression).ToString ():
//					break;
//				case typeof (AspNetServerComment).ToString ():
//					break;
//				case typeof (AspNetResourceExpression).ToString ():
//					break;
//				case typeof (AspNetDataBindingExpression).ToString ():
//					break;
//				}
				return string.Empty; // TODO: serialize AspNetDom nodes with the right end location
			}
			
			// checking for a ASP.NET server control OR HtmlControl
			if (element.Name.HasPrefix || IsRunAtServer (element)) {
				// create and add a Component to the Container
				string id = string.Empty;
				XName idName = new XName ("id");

				foreach (XAttribute attr in element.Attributes) {
					if (attr.Name.ToLower () == idName) {
						id = attr.Value;
						break;
					}
				}

				IComponent comp = null;
				try {
					comp = ProcessControl (element);
					if (comp != null)
						this.host.Container.Add (comp, id);
				} catch (Exception ex) {
					System.Diagnostics.Trace.WriteLine (ex.ToString ());
				}

				var control = comp as WebControl;

				// genarete placeholder
				if (control != null) {
					StringWriter strWriter = new StringWriter ();
					HtmlTextWriter writer = new HtmlTextWriter (strWriter);
					control.RenderControl (writer);
					writer.Close ();
					strWriter.Flush ();
					string content = strWriter.ToString ();
					strWriter.Close ();
					return content;
				}
			}
			
			// the node is a html element
			string output = "<" + element.Name.FullName;
			
			// print the attributes... TODO: watchout for runat="server"
			foreach (MonoDevelop.Xml.StateEngine.XAttribute attr in element.Attributes) {
				output += " " + attr.Name.FullName + "=\"" + attr.Value + "\"";
			}
			
			if (element.IsSelfClosing) {
				output += " />";
			} else {
				output += ">";
				
				// serializing the childnodes if any
				foreach (MonoDevelop.Xml.StateEngine.XNode nd in element.Nodes) {
					// get the text before the openning tag of the child element
					output += GetTextFromEditor (prevTagLocation, nd.Region.Begin);
					// and the element itself
					output += serializeNode (nd);
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
					output += GetTextFromEditor (new TextLocation (lastChildEndLine, lastChildEndColumn), element.ClosingTag.Region.Begin);
					prevTagLocation = element.ClosingTag.Region.End;
				} else {
					// TODO: the element is not closed. Warn the user
				}
				
				output += "</" + element.Name.FullName + ">";
			}
			
			return output;
		}

		private string GetTextFromEditor (TextLocation start, TextLocation end)
		{
			if (srcEditor == null)
				throw new NullReferenceException ("The SourceEditorView is not set. Can't process document for text nodes.");

			return srcEditor.TextEditor.GetTextBetween (start.Line, start.Column, end.Line, end.Column);
		}

		private IComponent ProcessControl (XElement element)
		{
			// get the control's Type
			System.Type controlType = null;

			if (element.Name.HasPrefix) {
				// assuming ASP.NET control
				var refMan = host.GetService (typeof(WebFormReferenceManager)) as WebFormReferenceManager;
				if (refMan == null) {
					throw new ArgumentNullException ("The WebFormReferenceManager service is not set");
				}
				string typeName = refMan.GetTypeName (element.Name.Prefix, element.Name.Name);
				controlType = typeof(System.Web.UI.WebControls.WebControl).Assembly.GetType (typeName, true, true);
			} else {
				// if no perfix was found. we have a HtmlControl
				controlType = GetHtmlControlType (element);
			}
			if (controlType == null)
				return null;
				//throw new Exception ("Could not determine the control type for element " + element.FriendlyPathRepresentation);

			IComponent component = Activator.CreateInstance (controlType) as IComponent;

			// Since we have no Designers the TypeDescriptorsFilteringService won't work :(
			// looking for properties and events declared as attributes of the server control node
			PropertyDescriptorCollection pCollection = TypeDescriptor.GetProperties (controlType);
			PropertyDescriptor desc = null;
			EventDescriptorCollection eCollection = TypeDescriptor.GetEvents (controlType);
			EventDescriptor evDesc = null;

			foreach (XAttribute attr in element.Attributes) {
				desc = pCollection.Find (attr.Name.Name, true);

				if (desc != null) {
					if (desc.PropertyType == typeof (string))
						desc.SetValue (component, attr.Value);
					else if (desc.PropertyType == typeof (uint)) {
						uint val = 0;
						uint.TryParse (attr.Value, out val);
						desc.SetValue (component, val);
					} else if (desc.PropertyType == typeof (int)) {
						int val = 0;
						int.TryParse (attr.Value, out val);
						desc.SetValue (component, val);
					} else if (desc.PropertyType == typeof (bool)) {
						bool val = false;
						bool.TryParse (attr.Value,out val);
						desc.SetValue (component, val);
					} else if (desc.PropertyType == typeof (System.Drawing.Color))
						desc.SetValue (component, System.Drawing.Color.FromName (attr.Value));
				} else if (attr.Name.Name.Contains ("On")) {
					// TODO: filter events for the component  !?
					string eventName = attr.Name.Name.Replace ("On", string.Empty);
					evDesc = eCollection.Find (eventName, true);
					if (evDesc != null) {

					}
				}
			}

			return component;
		}

		bool IsRunAtServer (XElement el)
		{
			XName runat = new XName ("runat");
			foreach (XAttribute a  in el.Attributes) {
				if ((a.Name.ToLower () == runat) && (a.Value.ToLower () == "server"))
					return true;
			}
			return false;
		}

		//static string[] htmlControlTags = {"a", "button", "input", "img", "select", "textarea"};
		static Dictionary<string, Type> htmlControlTags = new Dictionary<string, Type> () {
			{"a", typeof (HtmlAnchor)},
			{"button", typeof (HtmlButton)},
			{"input", null}, // we'll check that one in the ProcessHtmlControl, because for this tag we have a lot of possible types depending on the type attribute
			{"img", typeof (HtmlImage)},
			{"select", typeof (HtmlSelect)},
			{"textarea", typeof (HtmlTextArea)}
		};

		private Type GetHtmlControlType (XElement el)
		{
			string nameLowered = el.Name.Name.ToLower ();
			if (!htmlControlTags.ContainsKey (nameLowered))
				return null;

			Type compType = htmlControlTags[nameLowered];
			// we have an input tag
			if (compType == null) {
				string typeAttr = GetAttributeValueCI (el.Attributes, "type");
				switch (typeAttr.ToLower ()) {
				case "button":
					compType = typeof (HtmlInputButton);
					break;
				case "checkbox":
					compType = typeof (HtmlInputCheckBox);
					break;
				case "file":
					compType = typeof (HtmlInputFile);
					break;
				case "hidden":
					compType = typeof (HtmlInputHidden);
					break;
				case "image":
					compType = typeof (HtmlInputImage);
					break;
				case "password":
					compType = typeof (HtmlInputPassword);
					break;
				case "radio":
					compType = typeof (HtmlInputRadioButton);
					break;
				case "reset":
					compType = typeof (HtmlInputReset);
					break;
				case "submit":
					compType = typeof (HtmlInputSubmit);
					break;
				case "text":
					compType = typeof (HtmlInputText);
					break;
				}
			}

			return compType;
		}

		/// <summary>
		/// Gets the attribute value. case insensitive
		/// </summary>
		string GetAttributeValueCI (XAttributeCollection attributes, string key)
		{
			XName nameKey = new XName (key.ToLowerInvariant ());

			foreach (XAttribute attr in attributes) {
				if (attr.Name.ToLower () == nameKey)
					return attr.Value;
			}
			return string.Empty;
		}

		#endregion
		
		//we need this to invoke protected member before rendering
		private static MethodInfo onPreRenderMethodInfo;
		
		private static MethodInfo OnPreRenderMethodInfo {
			get {
				if (onPreRenderMethodInfo == null)
					onPreRenderMethodInfo = 
					typeof (Control).GetMethod ("OnPreRender", BindingFlags.NonPublic|BindingFlags.Instance);
				
				return onPreRenderMethodInfo;
			}
		}

		
		//we need this to invoke protected member before rendering
		private static MethodInfo onInitMethodInfo;
		
		private static MethodInfo OnInitMethodInfo {
			get {
				if (onInitMethodInfo == null)
					onInitMethodInfo = 
					typeof (Control).GetMethod ("OnInit", BindingFlags.NonPublic|BindingFlags.Instance);
				
				return onInitMethodInfo;
			}
		}
		
		#region add/remove/update controls
		
		// TODO: reimplement the actions on controls on the DOM tree and the designer container
		
		bool suppressAddControl = false;
		
		public void AddControl (Control control)
		{
			if (suppressAddControl) return;
			
			System.Console.WriteLine("AddControl method called");
			OnInitMethodInfo.Invoke (control, new object[] {EventArgs.Empty});
			//view.AddControl (control);
		}

		public void RemoveControl (Control control)
		{
			//view.RemoveControl (control);
		}
		
		public void RenameControl (string oldName, string newName)
		{
			//view.RenameControl (oldName, newName);
		}		
				
		public void InsertFragment (string fragment)
		{
			Control[] controls;
			string doc;
			//aspParser.ProcessFragment (fragment, out controls, out doc);
			//view.InsertFragment (doc);
			
			//FIXME: when controls are inserted en masse using InsertFragment, the designer surface
			//doesn't seem to display then properly till they've been updated
//			foreach (Control c in controls)
//				view.UpdateRender (c);
		}

		#endregion

		private string ConstructErrorDocument (string errorTitle, string errorDetails)
		{
			return "<html><body fgcolor='red'><h1>"
				+ errorTitle
				+ "</h1><p>"
				+ errorDetails
				+ "</p></body></html>";
		}

		#region Add/fetch general directives

		/// <summary>
		/// Adds a directive port tracking.
		/// </summary>
		/// <returns>A placeholder identifier that can be used in the document</returns>
		public string AddDirective (string name, IDictionary values)
		{
//			if ((0 == String.Compare (name, "Page", true, CultureInfo.InvariantCulture) && directives["Page"] != null)
//				|| (0 == String.Compare (name, "Control", true, CultureInfo.InvariantCulture) && directives["Control"] != null))
//				throw new Exception ("Only one Page or Control directive is allowed in a document");
//
//			DocumentDirective directive = new DocumentDirective (name, values, directivePlaceholderKey);
//			directivePlaceholderKey++;
//
//			if (directives[name] == null)
//				directives[name] = new ArrayList ();
//
//			((ArrayList)directives[name]).Add(directive);
//
//			return String.Format(DirectivePlaceholderStructure, directive.Key.ToString ());
			return string.Empty;
		}

		public string RemoveDirective (int placeholderId)
		{
			DocumentDirective directive = null;
			foreach (DictionaryEntry de in directives)
			{
				if (de.Value is DocumentDirective) {
					if (((DocumentDirective)de.Value).Key == placeholderId) {
						directive = (DocumentDirective)de.Value;
						directives.Remove(de.Key);
					}
				}
				else
					foreach (DocumentDirective d in (ArrayList)de.Value)
						if (d.Key == placeholderId) {
							directive = d;
							((ArrayList)de.Value).Remove (d);
							break;
						}
				if (directive != null)
					break;
			}

			if (directive == null)
				return string.Empty;
			return directive.ToString();
		}

		/// <summary>
		/// Gets all of the directives of a given type
		/// </summary>
		public DocumentDirective[] GetDirectives (string directiveType)
		{
			ArrayList localDirectiveList = new ArrayList ();
			foreach (DictionaryEntry de in directives)
			{
				if (de.Value is DocumentDirective)
				{
					if (0 == string.Compare (((DocumentDirective)de.Value).Name, directiveType, true, CultureInfo.InvariantCulture))
						localDirectiveList.Add (de.Value);
				}
				else
					foreach (DocumentDirective d in (ArrayList)de.Value)
						if (0 == string.Compare (directiveType, d.Name, true, CultureInfo.InvariantCulture))
							localDirectiveList.Add (d);
			}

			return (DocumentDirective[]) localDirectiveList.ToArray (typeof (DocumentDirective));
		}

		/// <summary>
		/// Gets the first directive of a given type
		/// </summary>
		/// <param name="create">Whether the directive should be created if one does not already exist</param>
		public DocumentDirective GetFirstDirective (string directiveType, bool create)
		{
			foreach (DictionaryEntry de in directives)
			{
				if (de.Value is DocumentDirective)
				{
					if (0 == string.Compare (((DocumentDirective)de.Value).Name, directiveType, true, CultureInfo.InvariantCulture))
						return (DocumentDirective) de.Value ;
				}
				else
					foreach (DocumentDirective d in (ArrayList)de.Value)
						if (0 == string.Compare (d.Name, directiveType, true, CultureInfo.InvariantCulture))
							return d;
			}

			//should directive be created if it can't be found?
			if (create) {
				AddDirective (directiveType, null);
				return GetFirstDirective (directiveType, false);
			}

			return null;
		}


		#endregion
	}
}
