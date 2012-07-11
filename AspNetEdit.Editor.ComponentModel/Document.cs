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
using System.IO;
using System.ComponentModel.Design;
using System.Collections;
using System.ComponentModel;
using System.Globalization;
using System.Reflection;

using MonoDevelop.Xml.StateEngine;
using MonoDevelop.AspNet;
using MonoDevelop.AspNet.Parser;
using MonoDevelop.AspNet.StateEngine;
using MonoDevelop.SourceEditor;

using ICSharpCode.NRefactory;

namespace AspNetEdit.Editor.ComponentModel
{
	public class Document
	{
		public static readonly string newDocument = "<html>\n<head>\n\t<title>{0}</title>\n</head>\n<body>\n<form runat=\"server\">\n\n</form></body>\n</html>";

		string document;
		Hashtable directives;
		private int directivePlaceholderKey = 0;

		private Control parent;
		private DesignerHost host;
		
		AspNetParsedDocument aspDoc;
		SourceEditorView srcEditor;
		
		///<summary>Creates a new document</summary>
		public Document (Control parent, DesignerHost host, string documentName)
		{
			initDocument (parent, host);
			this.document = String.Format (newDocument, documentName);
		}
		
		///<summary>Creates a document from an existing file</summary>
		public Document (Control parent, DesignerHost host, SourceEditorView srcEditorView, AspNetParsedDocument doc)
		{
			initDocument (parent, host);
			this.srcEditor = srcEditorView;
			this.aspDoc = doc;
			
			Control[] controls;
			
			//aspParser.ProcessFragment (document, out controls, out this.document);
			parse (srcEditor.Text, aspDoc.FileName);
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
			
			//this.aspParser = new DesignTimeParser (host, this);
			xDom = null;
		}

		#region StateEngine parser
		
		MonoDevelop.Xml.StateEngine.XDocument xDom;
		
		void parse (string doc, string fileName)
		{
			MonoDevelop.Xml.StateEngine.Parser parser = new MonoDevelop.Xml.StateEngine.Parser (
				new MonoDevelop.AspNet.StateEngine.AspNetFreeState (),
				true
			);

			using (StringReader strRd = new StringReader (doc)) {
				parser.Parse (strRd);
			}

			xDom = parser.Nodes.GetRoot ();
		}
		
		#endregion
		
		#region Designer communication
		

		public string ToDesignableHtml ()
		{
			string doc = null;
			if (xDom != null) {
				doc = serializeNode (xDom.RootElement);
			}

			return doc;
		}
		
		public string ToAspNetCode ()
		{
			// TODO: serialize the DOM tree to ASP.NET
			
			return string.Empty;
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
			
			// checking for a ASP.NET server control
			if (element.Name.HasPrefix && (element.Name.Prefix == "asp")) {
				// create and add a Component to the Container
				bool isRunAtServer = false;
				string id = string.Empty;
				XName runatName = new XName ("runat");
				XName idName = new XName ("id");
				
				foreach (XAttribute attr in element.Attributes) {
					if (attr.Name.ToLower () == runatName) {
						if (attr.Value == "server")
							isRunAtServer = true;
						else
							break;
					} else if (attr.Name.ToLower () == idName) {
						id = attr.Value;
					}
				}
				IComponent comp = null;
				if (isRunAtServer && (id != string.Empty)) {
					var refMan = host.GetService (typeof(WebFormReferenceManager)) as WebFormReferenceManager;
					if (refMan == null) {
						throw new ArgumentNullException ("The WebFormReferenceManager service is not set");
					}
					
					try {
						string typeName = refMan.GetTypeName (element.Name.Prefix, element.Name.Name);
						System.Type controlType = typeof(System.Web.UI.WebControls.WebControl).Assembly.GetType (typeName, true, true);
						comp = host.CreateComponent (controlType, id);
					} catch (Exception ex) {
						System.Diagnostics.Trace.WriteLine (ex.ToString ());
					}
				}

				var control = comp as WebControl;

				// genarete placeholder
				if (control != null) {
					StringWriter strWriter = new StringWriter ();
					HtmlTextWriter writer = new HtmlTextWriter (strWriter);
					control.Page.EnableEventValidation = false;
					control.RenderControl (writer);
					writer.Close ();
					strWriter.Flush ();
					string content = strWriter.ToString ();
					strWriter.Close ();
					return content;
				} else
					return string.Empty;
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
					output += srcEditor.TextEditor.GetTextBetween (
							prevTagLocation.Line,
							prevTagLocation.Column,
							nd.Region.BeginLine,
							nd.Region.BeginColumn
					);
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
					output += srcEditor.TextEditor.GetTextBetween (
						lastChildEndLine,
						lastChildEndColumn,
						element.ClosingTag.Region.BeginLine,
						element.ClosingTag.Region.BeginColumn
					);
					prevTagLocation = element.ClosingTag.Region.End;
				} else {
					// TODO: the element is not closed. Warn the user
				}
				
				output += "</" + element.Name.FullName + ">";
			}
			
			return output;
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
