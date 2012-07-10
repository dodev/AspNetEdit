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

//using AspNetEdit.Editor.Persistence;
//using AspNetEdit.Editor.ComponentModel;
//using AspNetEdit.Editor.UI;

using MonoDevelop.Xml;
using MonoDevelop.AspNet;
using MonoDevelop.AspNet.Parser;
using MonoDevelop.AspNet.Parser.Dom;
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

			CaseInsensitiveHashCodeProvider provider = new CaseInsensitiveHashCodeProvider (CultureInfo.InvariantCulture);
			CaseInsensitiveComparer comparer = new CaseInsensitiveComparer (CultureInfo.InvariantCulture);
			directives = new Hashtable (provider, comparer);
			
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
				} else {
					// TODO: the element is not closed. Warn the user
				}
				
				output += "</" + element.Name.FullName + ">";
			}
			
			return output;
		}
		
		#endregion
		
		#region Serialisation stuff
		
		///<summary>Converts a designer document fragment to ASP.NET code</summary>
		public string Serialize (string designerDocumentFragment)
		{
			if (host == null)
				throw new Exception("The document cannot be persisted without a host");
			
			string serializedDoc = string.Empty;
			StringWriter writer = new StringWriter ();
			
			//keep method argument meaningfully named, but keep code readable!
			string frag = designerDocumentFragment;
			int length = frag.Length;
			
			int pos = 0;
			SMode mode = SMode.Free;
			
			while (pos < length)
			{
				char c = frag [pos];
				
				switch (mode)
				{
					//it's freely copying to output, but watching for a directive or control placeholder 
					case SMode.Free:
						if (c == '<')
						{
							if ((pos + 10 < length) && frag.Substring (pos + 1, 10) == "aspcontrol") {
								mode = SMode.ControlId;
								pos += 10;
								break;
							}
							else if ((pos + 20 < length) && frag.Substring (pos + 1, 20) == "directiveplaceholder") {
								mode = SMode.DirectiveId;
								pos += 20;
								break;
							}
						}
						
						writer.Write (c);
						break;
					
					//it's found a directive placeholder and is scanning for the ID
					case SMode.DirectiveId:
						if (c == 'i' && (pos + 4 < length) && frag.Substring (pos, 4) == "id=\"") {
							int idEnd = frag.IndexOf ('"', pos + 4 + 1);
							if (idEnd == -1) throw new Exception ("Identifier was unterminated");
							int id  = Int32.Parse (frag.Substring (pos + 4, (idEnd - pos - 4)));
							
							//TODO: more intelligent removal/copying of directives in case of fragments
							//works fine with whole document.
							string directive = RemoveDirective (id);
							writer.Write (directive);				
							
							mode = SMode.DirectiveEnd;
							pos = idEnd;
						}
						break;
					
					//it's found a control placeholder and is scanning for the ID
					case SMode.ControlId:
						if (c == 'i' && (pos + 4 < length) && frag.Substring (pos, 4) == "id=\"") {
							int idEnd = frag.IndexOf("\"", pos + 4);
							if (idEnd == -1) throw new Exception ("Identifier was unterminated");
							string id  = frag.Substring (pos + 4, (idEnd - pos - 4));		
							
							DesignContainer dc = (DesignContainer) host.Container;
							Control control = dc.GetComponent (id) as Control;
							if (control == null) throw new Exception ("Could not retrieve control "+id);
							//ControlPersister.PersistControl (writer, control);
							
							mode = SMode.ControlEnd;
							pos = idEnd;
						}
						break;
					
					//it's found the control's ID and is looking for the end
					case SMode.ControlEnd:
						if (c == '<' && (pos + 13 < length) && frag.Substring (pos, 13) == "</aspcontrol>") {
							pos += 12;
							mode = SMode.Free;
						}
						break;
					
					//it's found the placeholder's ID and is looking for the end
					case SMode.DirectiveEnd:
						if (c == '/' && (pos + 2 < length) && frag.Substring (pos, 2) == "/>") {
							pos += 1;
							mode = SMode.Free;
						}
						break;
				}
				
				pos++;
			}
			
			serializedDoc = writer.ToString ();
			writer.Close ();

			return serializedDoc;
		}
		
		public void InitialiseControls (IEnumerable controls)
		{
			foreach (Control c in controls)
				InitialiseControl(c);
		}
		
		public static void InitialiseControl (Control control)
		{
			OnInitMethodInfo.Invoke (control, new object[] {EventArgs.Empty});
		}
		
		//modes for the Serializing parser
		private enum SMode {
			Free,
			ControlId,
			DirectiveId,
			ControlEnd,
			DirectiveEnd
		}
		
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
		
		///<summary>Renders the designer html for an ASP.NET Control</summary>
		public static string RenderDesignerControl (Control control)
		{
//			string height = "auto";
//			string width = "auto";
//			string canResize = "true";
//			string canDrop = "false";
//			string id = control.UniqueID;
//			
//			WebControl wc = control as WebControl;
//			if (wc != null) {
//				height = wc.Height.ToString ();
//				width = wc.Width.ToString ();
//			}
//			else
//			{
//				canResize = "false";
//			}
//			
//			//TODO: is there a better way to make tiny controls appear a decent size?
//			if (height == "" || height == "auto") height = "20px";
//			if (width == "" || width == "auto") width = "20px";
//			
//			//render the control
//			//TODO: use designer, when they're written
//			
//			OnPreRenderMethodInfo.Invoke (control, new object[] {EventArgs.Empty});
//			System.IO.StringWriter strWriter = new System.IO.StringWriter ();
//			System.Web.UI.HtmlTextWriter writer = new System.Web.UI.HtmlTextWriter (strWriter);
//			control.RenderControl (writer);
//			writer.Close ();
//			strWriter.Flush ();
//			string content = strWriter.ToString ();
//			strWriter.Close ();
//			
//			return string.Format (ControlSubstituteStructure, id, width, height, canDrop, canResize, content);
			return string.Empty;
		}
		
		#endregion
		
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
