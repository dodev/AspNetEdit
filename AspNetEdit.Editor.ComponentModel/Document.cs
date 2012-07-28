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
using System.Threading;

using MonoDevelop.Xml.StateEngine;
using MonoDevelop.AspNet;
using MonoDevelop.AspNet.Parser;
using MonoDevelop.AspNet.StateEngine;
using MonoDevelop.SourceEditor;

using ICSharpCode.NRefactory;
using ICSharpCode.NRefactory.TypeSystem;
using AspNetEdit.Tools;

namespace AspNetEdit.Editor.ComponentModel
{
	public class Document
	{
		public static readonly string newDocument = "<html>\n<head>\n\t<title>{0}</title>\n</head>\n<body>\n<form runat=\"server\">\n\n</form></body>\n</html>";

		Hashtable directives;
		int directivePlaceHolderKey = 0;

		private Control parent;
		private DesignerHost host;

		AspNetParser parser;
		AspNetParsedDocument aspNetDoc;
		ExtensibleTextEditor textEditor;
		// notes when the content of the textEditor doesn't match the content of the XDocument
		bool txtDocDirty; 

		ManualResetEvent updateEditorContent;
		
		///<summary>Creates a new document</summary>
		public Document (Control parent, DesignerHost host, string documentName)
		{
			initDocument (parent, host);
			Parse (String.Format (newDocument, documentName), documentName);
			// TODO: get a ExtensibleTextEditor instance, if we have an new empty file
		}
		
		///<summary>Creates a document from an existing file</summary>
		public Document (Control parent, DesignerHost host, ExtensibleTextEditor txtEditor)
		{
			textEditor = txtEditor;
			initDocument (parent, host);
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

			parser = new AspNetParser ();
			directives = null;
			aspNetDoc = null;
			txtDocDirty = true;
			updateEditorContent = new ManualResetEvent (true);
		}

		public bool TxtDocDirty {
			set {
				if (value) {
					updateEditorContent.Set ();
					OnChanged ();
				} else
					updateEditorContent.Reset ();

				txtDocDirty = value;
			}
			get { return txtDocDirty; }
		}

//		public void PersistDocument ()
//		{
//			System.Threading.Thread worker = new System.Threading.Thread (new System.Threading.ThreadStart(StartPersistingDocument));
//			worker.Start ();
//		}
//
//		public void StartPersistingDocument ()
//		{
//			OnChanging ();
//			try {
//				// wait until the there have been made changes to the editor
//				updateEditorContent.WaitOne ();
//
//				// parse the contents of the textEditor
//				Parse ();
//	
//				// initializing the dicts of directives and controls tags
//				if (directives == null) {
//					directives = new Hashtable (StringComparer.InvariantCultureIgnoreCase);
//					CheckForDirective (aspNetDoc.XDocument.AllDescendentNodes);
//					ParseControls ();
//				}
//	
//				// serialize the tree to designable HTML
//				//designableHtml = serializeNode (aspNetDoc.XDocument.RootElement);
//			} catch (Exception ex) {
//				System.Diagnostics.Trace.WriteLine (ex.ToString ());
//			} finally {
//				// set the event to not signaled
//				updateEditorContent.Reset ();
//			}
//
//			OnChanged ();
//		}


		#region StateEngine parser

		/// <summary>
		/// Parse the TextEditor.Text document and tracks the txtDocDirty flag.
		/// </summary>
		public AspNetParsedDocument Parse ()
		{
			if (txtDocDirty) {
				aspNetDoc = Parse (textEditor.Text, textEditor.FileName);
				txtDocDirty = false;
			}
			return aspNetDoc;
		}

		AspNetParsedDocument Parse (string doc, string fileName)
		{
			AspNetParsedDocument parsedDoc = null;
			using (StringReader strRd = new StringReader (doc)) {
				parsedDoc = parser.Parse (true, fileName, strRd, textEditor.Project) as AspNetParsedDocument;
			}
			return parsedDoc;
		}

		void CheckForDirective (IEnumerable<XNode> nodes)
		{
			foreach (XNode node in nodes) {
				if (node is XContainer) {
					var container = node as XContainer;
					CheckForDirective (container.AllDescendentNodes);
	
				} else if (node is AspNetDirective) {
					var directive = node as AspNetDirective;
					var properties = new Hashtable (StringComparer.InvariantCultureIgnoreCase);
					foreach (XAttribute attr in directive.Attributes)
						properties.Add (attr.Name.Name, attr.Value);
					AddDirective (directive.Name.Name, properties);
				}
			}
		}

		void ParseControls ()
		{
			// the method check for control may change the document
			// so we parse the document each time it does
			do {
				var doc = Parse ();
				CheckForControl (doc.XDocument.RootElement);
			} while (txtDocDirty);
		}

		void CheckForControl (XNode node)
		{
			if (!(node is XElement))
				return;

			var element = node as XElement;

			if (element.Name.HasPrefix || XDocumentHelper.IsRunAtServer (element)) {
				string id = XDocumentHelper.GetAttributeValueCI (element.Attributes, "id");

				try {
					// check the DesignContainer if a component for that node already exists
					if (string.IsNullOrEmpty(id) || (host.GetComponent(id) == null)) {
						IComponent comp = ProcessControl (element);
						if (comp != null) {
							this.host.Container.Add (comp, id);
	
							// add id to the component, for later recognition
							if (id == string.Empty) {
								host.AspNetSerializer.SetAttribtue (element, "id", comp.Site.Name);
								return;
							}
						}
					}

				} catch (Exception ex) {
					System.Diagnostics.Trace.WriteLine (ex.ToString ());
				}
			}

			foreach (XNode nd in element.Nodes) {
				if (txtDocDirty)
					return;

				CheckForControl (nd);
			}
				
		}

		public void ReplaceText (DomRegion region, string newValue)
		{
			Gtk.Application.Invoke (delegate {
				textEditor.Remove (region);
				textEditor.SetCaretTo (region.BeginLine, region.BeginColumn);
				textEditor.InsertAtCaret (newValue);

				TxtDocDirty = true;
			});
		}

		public void RemoveText (DomRegion region)
		{
			Gtk.Application.Invoke (delegate {
				textEditor.Remove (region);

				TxtDocDirty = true;
			});
		}

		public void InsertText (TextLocation loc, string text)
		{
			Gtk.Application.Invoke (delegate {
				textEditor.SetCaretTo (loc.Line, loc.Column);
				textEditor.InsertAtCaret (text);

				TxtDocDirty = true;
			});
		}


		#endregion
		
		#region Designer communication

		public event EventHandler Changing;
		public event EventHandler Changed;

		public void OnChanged ()
		{
			if (Changed != null)
				Changed (this, EventArgs.Empty);
		}

		public void OnChanging ()
		{
			if (Changing != null)
				Changing (this, EventArgs.Empty);
		}

		#endregion

		public string GetTextFromEditor (TextLocation start, TextLocation end)
		{
			return GetTextFromEditor (start.Line, start.Column, end.Line, end.Column);
		}

		public string GetTextFromEditor (int startLine, int startColumn, int endLine, int endColumn)
		{
			if (textEditor == null)
				throw new NullReferenceException ("The SourceEditorView is not set. Can't process document for text nodes.");

			return textEditor.GetTextBetween (startLine, startColumn, endLine, endColumn);
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
			if ((controlType == null) || (controlType == typeof (ListItem)))
				return null;
				//throw new Exception ("Could not determine the control type for element " + element.FriendlyPathRepresentation);

			IComponent component = Activator.CreateInstance (controlType) as IComponent;

			if (component is ListControl)
				ParseListItems (component as ListControl, element);

			if ((component is HtmlContainerControl) && !element.IsSelfClosing) {
				var containerControl = component as HtmlContainerControl;
				containerControl.InnerHtml = GetTextFromEditor (element.Region.End, element.ClosingTag.Region.Begin);
			}

			// Since we have no Designers the TypeDescriptorsFilteringService won't work :(
			// looking for properties and events declared as attributes of the server control node
			Attribute[] filter = new Attribute[] { BrowsableAttribute.Yes};
			PropertyDescriptorCollection pCollection = TypeDescriptor.GetProperties (controlType, filter);
			PropertyDescriptor desc = null;
//			EventDescriptorCollection eCollection = TypeDescriptor.GetEvents (controlType, filter);
//			EventDescriptor evDesc = null;

			foreach (XAttribute attr in element.Attributes) {
				desc = pCollection.Find (attr.Name.Name, true);
				if ((desc != null) && !desc.IsReadOnly) {
					var converter = TypeDescriptor.GetConverter (desc.PropertyType) as TypeConverter;
					if (converter != null) {
						desc.SetValue (component, converter.ConvertFromString (attr.Value));
					} else {
						throw new NotSupportedException ("No TypeConverter found for property of type " + desc.PropertyType.Name);
					}
				} //else if (attr.Name.Name.Contains ("On")) {
					// TODO: filter events for the component  !?
//					string eventName = attr.Name.Name.Replace ("On", string.Empty);
//					evDesc = eCollection.Find (eventName, true);
//					if (evDesc != null) {
//
//					}
//				}
			}

			return component;
		}

		void ParseListItems (ListControl lControl, XElement tag)
		{
			string text, value, innerHtml, textPropery, valuePropery;
			bool selected, enabled;
			var boolConverter = TypeDescriptor.GetConverter (typeof (bool)) as BooleanConverter;
			foreach (XElement el in tag.AllDescendentElements) {
				if (el.Name.Name.ToLower () != "listitem")
					continue;

				text = value = innerHtml = String.Empty;
				textPropery = valuePropery = String.Empty;
				selected = false;
				enabled = true;

				foreach (XAttribute attr in el.Attributes) {
					switch (attr.Name.Name.ToLower ()) {
					case "text":
						text = attr.Value;
						break;
					case "value":
						value = attr.Value;
						break;
					case "selected":
						selected = (bool)boolConverter.ConvertFromString (attr.Value);
						break;
					case "enabled":
						enabled = (bool)boolConverter.ConvertFromString (attr.Value);
						break;
					}
				}

				if (!el.IsSelfClosing)
					innerHtml = GetTextFromEditor (el.Region.End, el.ClosingTag.Region.Begin);

				if (!String.IsNullOrEmpty (innerHtml))
					textPropery = innerHtml;
				else if (!String.IsNullOrEmpty (text))
					textPropery = text;
				else if (!String.IsNullOrEmpty (value))
					textPropery = value;

				if (!String.IsNullOrEmpty (value))
					valuePropery = value;
				else if (!String.IsNullOrEmpty (innerHtml))
					valuePropery = innerHtml;
				else if (!String.IsNullOrEmpty (text))
					valuePropery = text;

				ListItem li = new ListItem (textPropery, valuePropery, enabled);
				li.Selected = selected;
				lControl.Items.Add (li);
			}
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
				string typeAttr = XDocumentHelper.GetAttributeValueCI (el.Attributes, "type");
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
			if ((0 == String.Compare (name, "Page", true, CultureInfo.InvariantCulture) && directives["Page"] != null)
				|| (0 == String.Compare (name, "Control", true, CultureInfo.InvariantCulture) && directives["Control"] != null))
				throw new Exception ("Only one Page or Control directive is allowed in a document");

			DocumentDirective directive = new DocumentDirective (name, values, directivePlaceHolderKey);
			directivePlaceHolderKey++;

			if (directives[name] == null)
				directives[name] = new ArrayList ();

			((ArrayList)directives[name]).Add(directive);

			// TODO: placeholder for directives
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

		public void Destroy ()
		{
			updateEditorContent.Dispose ();
		}
	}
}
