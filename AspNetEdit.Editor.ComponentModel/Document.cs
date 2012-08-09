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

using MonoDevelop.Ide;
using MonoDevelop.Xml.StateEngine;
using MonoDevelop.AspNet;
using MonoDevelop.AspNet.Parser;
using MonoDevelop.AspNet.StateEngine;
using MonoDevelop.SourceEditor;
using MonoDevelop.Ide.Gui.Content;
using ICSharpCode.NRefactory;
using ICSharpCode.NRefactory.TypeSystem;

using AspNetEdit.Tools;
using System.ComponentModel.Design.Serialization;

namespace AspNetEdit.Editor.ComponentModel
{
	public class Document
	{
		public static readonly string newDocument = "<html>\n<head>\n\t<title>{0}</title>\n</head>\n<body>\n<form runat=\"server\">\n\n</form></body>\n</html>";

		Hashtable directives;
		int directivePlaceHolderKey = 0;

		private Control parent;
		private DesignerHost host;

		ExtensibleTextEditor textEditor;

		// undo/redo wrapper of the SourceEditorView's funcitionality 
		UndoTracker undoTracker;
		IUndoHandler undoHandler;

		// notes when the content of the textEditor doesn't match the content of the XDocument
		bool txtDocDirty;
		// do not serialize the document to HTML
		bool suppressSerialization;
		// blocks threads from parsing the document when it been edited
		ManualResetEvent updateEditorContent;
		
		///<summary>Creates a new document</summary>
		public Document (Control parent, DesignerHost host, string documentName)
		{
			initDocument (parent, host);
			//newDocument. this.textEditor
			//Parse (String.Format (newDocument, documentName), documentName);
			// TODO: get a ExtensibleTextEditor instance, if we have an new empty file
		}
		
		///<summary>Creates a document from an existing file</summary>
		public Document (Control parent, DesignerHost host)
		{
			initDocument (parent, host);
		}
		
		private void initDocument (Control parent, DesignerHost host)
		{
			textEditor = MonoDevelop.Ide.IdeApp.Workbench.ActiveDocument.PrimaryView.GetContent<SourceEditorView> ().TextEditor;

			System.Diagnostics.Trace.WriteLine ("Creating document...");
			if (!(parent is WebFormPage))
				throw new NotImplementedException ("Only WebFormsPages can have a document for now");
			this.parent = parent;
			this.host = host;
			
			if (!host.Loading)
				throw new InvalidOperationException ("The document cannot be initialised or loaded unless the host is loading"); 

			directives = new Hashtable (StringComparer.InvariantCultureIgnoreCase);
			txtDocDirty = true;
			suppressSerialization = false;
			updateEditorContent = new ManualResetEvent (true);
			undoTracker = new UndoTracker ();
			undoHandler = IdeApp.Workbench.ActiveDocument.PrimaryView.GetContent <IUndoHandler> ();
			if (undoHandler == null)
				throw new NullReferenceException ("Could not obtain the IUndoHandler from the SourceEditorView");
		}

		public void InitControlsAndDirectives ()
		{
			// check the document for directives
			ParseDirectives ();

			// check for controls and add them to the design container
			ParseControls ();
		}

		#region Event firing control

		bool TxtDocDirty {
			set {
				txtDocDirty = value;

				if (value) {
					//OnChanged ();
				}
			}
			get { return txtDocDirty; }
		}

		public void WaitForChanges ()
		{
			suppressSerialization = true;
			//pendingChanges.Reset ();
		}

		public void CommitChanges ()
		{
			suppressSerialization = false;
			OnChanged ();
		}

		#endregion

		#region StateEngine parser

		/// <summary>
		/// Parse the TextEditor.Text document and tracks the txtDocDirty flag.
		/// </summary>
		public AspNetParsedDocument Parse ()
		{
			// waiting if someone is about to change the contents of the document
			updateEditorContent.WaitOne ();
			if (TxtDocDirty) {
				IdeApp.Workbench.ActiveDocument.UpdateParseDocument ();
				TxtDocDirty = false;
			}
			return IdeApp.Workbench.ActiveDocument.ParsedDocument as AspNetParsedDocument;
		}

		AspNetParsedDocument Parse (string doc, string fileName)
		{
			AspNetParsedDocument parsedDoc = null;
			AspNetParser parser = new AspNetParser ();
			using (StringReader strRd = new StringReader (doc)) {
				parsedDoc = parser.Parse (true, fileName, strRd, textEditor.Project) as AspNetParsedDocument;
			}
			return parsedDoc;
		}


		void ParseDirectives ()
		{
			var doc = Parse ();
			foreach (XNode node in doc.XDocument.AllDescendentNodes) {
				if (node is AspNetDirective) {
					var directive = node as AspNetDirective;
					var properties = new Hashtable (StringComparer.InvariantCultureIgnoreCase);
					foreach (XAttribute attr in directive.Attributes)
						properties.Add (attr.Name.Name, attr.Value);
					AddDirective (directive.Name.Name, properties);
				}
			}
		}

		/// <summary>
		/// Checks the document for control tags, creates components and adds the to the IContainer.
		/// Adds id attributes to tags that are server controls but doesn't have it.
		/// </summary>
		void ParseControls ()
		{
			// no need to serialize the document, if we add just an id attribute to a control
			suppressSerialization = true;

			// the method check for control may change the document
			// so we parse the document each time it does
			do {
				var doc = Parse ();

				foreach (XNode node in doc.XDocument.RootElement.AllDescendentElements) {
					if (!(node is XElement))
						continue;
		
					var element = node as XElement;
		
					if (element.Name.HasPrefix || XDocumentHelper.IsRunAtServer (element)) {
						string id = XDocumentHelper.GetAttributeValueCI (element.Attributes, "id");
		
						// check the DesignContainer if a component for that node already exists
						if (host.GetComponent(id) == null) {
							IComponent comp = ProcessControl (element);

							if (comp == null)
								continue;
	
							// add id to the component, for later recognition if it has not ID
							if (String.IsNullOrEmpty(id)) {
								var nameServ = host.GetService (typeof (INameCreationService)) as INameCreationService;
								if (nameServ == null)
									throw new Exception ("Could not obtain INameCreationService from the DesignerHost.");

								host.AspNetSerializer.SetAttribtue (element, "id", nameServ.CreateName (host.Container, comp.GetType ()));
								updateEditorContent.WaitOne (); // wait until the changes have been applied to the document
								break;
							}

							this.host.Container.Add (comp, id);
							ProcessControlProperties (element, comp);
						}
					}
				}
			} while (txtDocDirty);

			suppressSerialization = false;
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

			return Activator.CreateInstance (controlType) as IComponent;
		}

		/// <summary>
		/// Parses the control's tag and sets the properties of the corresponding component
		/// </summary>
		/// <param name='element'>
		/// The ASP.NET tag
		/// </param>
		/// <param name='component'>
		/// The sited component
		/// </param>
		void ProcessControlProperties (XElement element, IComponent component)
		{
			ProcessControlProperties (element, component, false);
		}

		/// <summary>
		/// Parses the control's tag and sets the properties of the corresponding component
		/// + checking and setting to default value, the properties which are not explicitly set
		/// in the ASP.NET code
		/// </summary>
		/// <param name='element'>
		/// The ASP.NET tag
		/// </param>
		/// <param name='component'>
		/// The sited component
		/// </param>
		/// <param name='checkForDefaults'>
		/// Filter the non-explicitly defined properties and set them to the default value.
		/// </param>
		void ProcessControlProperties (XElement element, IComponent component, bool checkForDefaults)
		{
			if (component is ListControl)
				ParseListItems (component as ListControl, element);

			if ((component is HtmlContainerControl) && !element.IsSelfClosing) {
				var containerControl = component as HtmlContainerControl;
				containerControl.InnerHtml = GetTextFromEditor (element.Region.End, element.ClosingTag.Region.Begin);
			}

			Attribute[] filter = new Attribute[] { BrowsableAttribute.Yes, ReadOnlyAttribute.No };
			PropertyDescriptorCollection pCollection = TypeDescriptor.GetProperties (component.GetType (), filter);
			PropertyDescriptor desc = null;
			EventDescriptorCollection eCollection = TypeDescriptor.GetEvents (component.GetType (), filter);
			EventDescriptor evDesc = null;
			List<PropertyDescriptor> explicitDeclarations = new List<PropertyDescriptor> ();

			foreach (XAttribute attr in element.Attributes) {
				desc = pCollection.Find (attr.Name.Name, true);
				// if we have an event attribute
				if (desc == null && CultureInfo.InvariantCulture.CompareInfo.IsPrefix (attr.Name.Name.ToLower (), "on")) {
					IEventBindingService iebs = host.GetService (typeof(IEventBindingService)) as IEventBindingService;
					if (iebs == null)
						throw new Exception ("Could not obtain IEventBindingService from host");

					string eventName = attr.Name.Name.Remove (0, 2);
					evDesc = eCollection.Find (eventName, true);

					if (evDesc != null)
						desc = iebs.GetEventProperty (evDesc);
				}

				if (desc == null)
					continue;
				//throw new Exception ("Could not find property " + attr.Name.Name + " of type " + component.GetType ().ToString ());

				desc.SetValue (component, desc.Converter.ConvertFromString (attr.Value));

				// add it to the properties that are defined in asp.net code
				if (checkForDefaults)
					explicitDeclarations.Add (desc);
			}

			// find properties not defined as attributes in the element and set them to the default value
			if (checkForDefaults) {
				foreach (PropertyDescriptor pDesc in pCollection) {
					if (!explicitDeclarations.Contains (pDesc))
						continue;

					object currVal = pDesc.GetValue (component);

					if (!pDesc.Attributes.Contains (new DefaultValueAttribute (currVal)))
						continue;

					pDesc.SetValue (component, (pDesc.Attributes [typeof (DefaultValueAttribute)] as DefaultValueAttribute).Value);
				}
			}
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

		/// <summary>
		/// Check if the state of all the components in the container matches
		/// their the that defined in their elements attributes
		/// </summary>
		public void PersistControls ()
		{
			var doc = Parse ();

			foreach (XNode node in doc.XDocument.RootElement.AllDescendentElements) {
				if (!(node is XElement))
					continue;
	
				var element = node as XElement;
	
				if (element.Name.HasPrefix || XDocumentHelper.IsRunAtServer (element)) {
					string id = XDocumentHelper.GetAttributeValueCI (element.Attributes, "id");

					bool checkDefaults;
					IComponent comp = host.GetComponent(id);
					if (comp == null) {
						// adding a new component
						comp = ProcessControl (element);

						if (comp == null)
							continue;

						// assuming that we have an id already
						host.Container.Add (comp, id);
						checkDefaults = false;
					} else {
						checkDefaults = true;
					}

					ProcessControlProperties (element, comp, checkDefaults);
				}
			}
		}

		#endregion
		
		#region Designer communication

		public event EventHandler Changing;
		public event EventHandler Changed;
		public event EventHandler UndoRedo;

		public void OnChanged ()
		{

			if ((Changed != null) && !suppressSerialization && txtDocDirty)
				Changed (this, EventArgs.Empty);
		}

		public void OnChanging ()
		{
			if (Changing != null)
				Changing (this, EventArgs.Empty);
		}

		public void OnUndoRedo ()
		{
			if (UndoRedo != null)
				UndoRedo (this, EventArgs.Empty);
		}

		#endregion

		#region TextEditor manipulation

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

		public void ReplaceText (DomRegion region, string newValue)
		{
			if (MonoDevelop.Ide.DispatchService.IsGuiThread)
				ReplaceTextWorker (region, newValue);
			else {
				ManualResetEventSlim finished = new ManualResetEventSlim (false);
				Gtk.Application.Invoke (delegate {
					ReplaceTextWorker (region, newValue);
					finished.Set ();
				});
				finished.Wait ();
			}
		}

		void ReplaceTextWorker (DomRegion region, string newValue)
		{
			// do not parse the document until changes have been made to the text
			updateEditorContent.Reset ();

			textEditor.SetCaretTo (region.BeginLine, region.BeginColumn);
			textEditor.Replace (textEditor.Caret.Offset, GetTextFromEditor (region.Begin, region.End).Length, newValue);

			undoTracker.FinishAction ();

			// let the parser know that the content is dirty and set the event
			TxtDocDirty = true;
			updateEditorContent.Set ();
		}

		public void RemoveText (DomRegion region)
		{
			if (MonoDevelop.Ide.DispatchService.IsGuiThread)
				RemoveTextWorker (region);
			else {
				ManualResetEventSlim finished = new ManualResetEventSlim (false);
				Gtk.Application.Invoke (delegate {
					RemoveTextWorker (region);
					finished.Set ();
				});
				finished.Wait ();
			}
		}

		void RemoveTextWorker (DomRegion region)
		{
			updateEditorContent.Reset ();

			textEditor.Remove (region);

			undoTracker.FinishAction ();

			TxtDocDirty = true;
			updateEditorContent.Set ();
		}

		public void InsertText (TextLocation loc, string text)
		{
			if (MonoDevelop.Ide.DispatchService.IsGuiThread)
				InsertTextWorker (loc, text);
			else {
				ManualResetEventSlim finished = new ManualResetEventSlim (false);
				Gtk.Application.Invoke (delegate {
					InsertTextWorker (loc, text);
					finished.Set ();
				});
				finished.Wait ();
			}
		}

		void InsertTextWorker (TextLocation loc, string text)
		{
			updateEditorContent.Reset ();

			textEditor.SetCaretTo (loc.Line, loc.Column);
			textEditor.InsertAtCaret (text);

			undoTracker.FinishAction ();

			TxtDocDirty = true;
			updateEditorContent.Set ();
		}

		#endregion

		#region Undo/Redo wrapper

		public void Undo ()
		{
			if (undoTracker.CanUndo) {
				undoHandler.Undo ();
				undoTracker.UndoAction ();
				OnUndoRedo ();
			}
		}

		public void Redo ()
		{
			if (undoTracker.CanRedo) {
				undoHandler.Redo ();
				undoTracker.RedoAction ();
				OnUndoRedo ();
			}
		}

		public bool CanUndo ()
		{
			return undoTracker.CanUndo;
		}

		public bool CanRedo ()
		{
			return undoTracker.CanRedo;
		}

		#endregion Undo/Redo wrapper

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
//			Control[] controls;
//			string doc;
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
