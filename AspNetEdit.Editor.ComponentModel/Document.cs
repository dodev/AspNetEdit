/* 
* Document.cs - Represents the DesignerHost's document
* 
* Authors: 
*  Michael Hutchinson <m.j.hutchinson@gmail.com>
*  Petar Dodev <petar.dodev@gmail.com>
*  
* Copyright (C) 2005 Michael Hutchinson
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

		// the TextEditor instance of the Source code view
		// all changes made in the designer are directly serialized
		// to text editor contents
		ExtensibleTextEditor textEditor;

		// undo/redo wrapper of the SourceEditorView's funcitionality 
		UndoTracker undoTracker;
		IUndoHandler undoHandler;

		// notes when the content of the textEditor doesn't match the content of the XDocument
		bool txtDocDirty;
		// should the the Changed event be fired
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
			System.Diagnostics.Trace.WriteLine ("Creating document...");

			// get the ExtensibleTextEditor instance of the Source code's view
			textEditor = IdeApp.Workbench.ActiveDocument.PrimaryView.GetContent<SourceEditorView> ().TextEditor;

			if (!(parent is WebFormPage))
				throw new NotImplementedException ("Only WebFormsPages can have a document for now");
			this.parent = parent;
			this.host = host;
			
			if (!host.Loading)
				throw new InvalidOperationException ("The document cannot be initialised or loaded unless the host is loading"); 

			directives = new Hashtable (StringComparer.InvariantCultureIgnoreCase);
			txtDocDirty = true;
			suppressSerialization = false;
			// create and set the event, to let the parser run the first time
			updateEditorContent = new ManualResetEvent (true);
			undoTracker = new UndoTracker ();
			undoHandler = IdeApp.Workbench.ActiveDocument.PrimaryView.GetContent <IUndoHandler> ();
			if (undoHandler == null)
				throw new NullReferenceException ("Could not obtain the IUndoHandler from the SourceEditorView");
		}

		/// <summary>
		/// Inits the controls list and the directives hashtable
		/// </summary>
		/// <description>
		/// This method runs only during the first load of the document.
		/// Then, controls and directives are pesisted using the add/remove/edit methods.
		/// </description>
		public void InitControlsAndDirectives ()
		{
			// check the document for directives
			ParseDirectives ();

			// check for controls and add them to the design container
			ParseControls ();
		}

		#region Event firing control

		/// <summary>
		/// Gets or sets a value indicating whether this <see cref="AspNetEdit.Editor.ComponentModel.Document"/> text document dirty.
		/// </summary>
		/// <value>
		/// <c>true</c> if text document dirty; otherwise, <c>false</c>.
		/// </value>
		/// <remarks>
		/// fires the Changed event when set to true
		/// </remarks>
		bool TxtDocDirty {
			set {
				txtDocDirty = value;

				if (value) {
					//OnChanged ();
				}
			}
			get { return txtDocDirty; }
		}

		// do not fire the Changed event
		public void WaitForChanges ()
		{
			suppressSerialization = true;
		}

		// fire the Changed event
		public void CommitChanges ()
		{
			suppressSerialization = false;
			OnChanged ();
		}

		#endregion

		#region StateEngine parser

		/// <summary>
		/// Gets the parsed document from the background parser
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

		/// <summary>
		/// Parses a given string
		/// </summary>
		/// <param name='doc'>
		/// ASP.NET document
		/// </param>
		/// <param name='fileName'>
		/// File name.
		/// </param>
		AspNetParsedDocument Parse (string doc, string fileName)
		{
			AspNetParsedDocument parsedDoc = null;
			AspNetParser parser = new AspNetParser ();
			using (StringReader strRd = new StringReader (doc)) {
				parsedDoc = parser.Parse (true, fileName, strRd, textEditor.Project) as AspNetParsedDocument;
			}
			return parsedDoc;
		}

		/// <summary>
		/// Checks the document for directives and adds them to the directives hashtable
		/// </summary>
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
		/// Adds an id attributes to tags that are server controls but doesn't have an id attribute.
		/// </summary>
		void ParseControls ()
		{
			// no need to serialize the document, if we add just an id attribute to a control
			suppressSerialization = true;

			// if an id tag was added the document changes
			// so we parse the document each time it does
			do {
				// get a fresh new AspNetParsedDocument
				var doc = Parse ();

				// go through all the nodes of the document
				foreach (XNode node in doc.XDocument.RootElement.AllDescendentElements) {
					// if a node is not a XElement, no need to check if it's a control
					if (!(node is XElement))
						continue;
		
					var element = node as XElement;
		
					// the controls have a tag prefix or runat="server" attribute
					if (element.Name.HasPrefix || XDocumentHelper.IsRunAtServer (element)) {
						string id = XDocumentHelper.GetAttributeValueCI (element.Attributes, "id");
		
						// check the DesignContainer if a component for that node already exists
						if (host.GetComponent(id) == null) {
							// create a component of type depending of the element
							IComponent comp = ProcessControl (element);

							if (comp == null)
								continue;
	
							// add id to the component, for later recognition if it has no ID attribute
							if (String.IsNullOrEmpty(id)) {
								var nameServ = host.GetService (typeof (INameCreationService)) as INameCreationService;
								if (nameServ == null)
									throw new Exception ("Could not obtain INameCreationService from the DesignerHost.");

								// insert the attribute to the element
								host.AspNetSerializer.SetAttribtue (element, "id", nameServ.CreateName (host.Container, comp.GetType ()));
								updateEditorContent.WaitOne (); // wait until the changes have been applied to the document
								break;
							}

							// we have a control component, add it to the container
							this.host.Container.Add (comp, id);
							// and parse its attributes for component properties
							ProcessControlProperties (element, comp);
						}
					}
				}
			} while (txtDocDirty);

			suppressSerialization = false;
		}

		/// <summary>
		/// Creates a component for a given XElement
		/// </summary>
		/// <returns>
		/// The control.
		/// </returns>
		/// <param name='element'>
		/// The IComponent if the element was a ASP.NET or HTML control, and null if it wasn't
		/// </param>
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
		/// </summary>
		/// <description>
		/// Parses the control's tag's attributes and sets the component's properties.
		/// The method can filter all the properties which are not explicitly set as attributes
		/// and set them to their default value. That functionality is useful when an undo 
		/// is performed and we have to revert the component's stage.
		/// </description>
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

			// get only the properties that can be browsed through the property grid and that are not read-only
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

					// remove the "on" prefix from the attribute's name
					string eventName = attr.Name.Name.Remove (0, 2);

					// try to find an event descriptor with that name
					evDesc = eCollection.Find (eventName, true);
					if (evDesc != null)
						desc = iebs.GetEventProperty (evDesc);
				}

				if (desc == null)
					continue;
				//throw new Exception ("Could not find property " + attr.Name.Name + " of type " + component.GetType ().ToString ());

				desc.SetValue (component, desc.Converter.ConvertFromString (attr.Value));

				// add the descriptor to the properties which are defined in the tag
				if (checkForDefaults)
					explicitDeclarations.Add (desc);
			}

			// find properties not defined as attributes in the element's tag and set them to the default value
			if (checkForDefaults) {
				// go through all the properties in the collection
				foreach (PropertyDescriptor pDesc in pCollection) {
					// the property is explicitly defined in the contrl's tag. skip it
					if (explicitDeclarations.Contains (pDesc))
						continue;

					// check if the component has it's default value. if yes - skip it
					object currVal = pDesc.GetValue (component);
					if (pDesc.Attributes.Contains (new DefaultValueAttribute (currVal)))
						continue;

					object defVal = (pDesc.Attributes [typeof (DefaultValueAttribute)] as DefaultValueAttribute).Value;

					// some of the default values attributes are set in different types
					if (!pDesc.PropertyType.IsAssignableFrom (defVal.GetType ())) {
						// usually the default value that mismatches the type of the property is a string
						if (defVal.GetType () != typeof (String))
							continue;

						// if it's an empty string and the property is an integer we have a problem
						// the empty string is usually interpreted as -1
						// FIXME: find a not so hacky solution for the problem
						if (pDesc.PropertyType.IsAssignableFrom (typeof (Int32)) && String.IsNullOrEmpty ((string) defVal)) {
							defVal = (object) -1;
						} else {
							// finally we have string which we can convert with the help of the property's typeconver
							defVal = pDesc.Converter.ConvertFromString ((string)defVal);
						}
					}

					// finally, set the default value to the property
					pDesc.SetValue (component, defVal);
				}
			}
		}

		/// <summary>
		/// Parses a list control tag for it's ListItems.
		/// </summary>
		/// <description>
		/// Parses the tag for child ListItem tags and adds the to the
		/// component's Items list.
		/// </description>
		/// <param name='lControl'>
		/// the ListControl's component
		/// </param>
		/// <param name='tag'>
		/// the ListControl's tag
		/// </param>
		void ParseListItems (ListControl lControl, XElement tag)
		{
			string text, value, innerHtml, textPropery, valuePropery;
			bool selected, enabled;
			var boolConverter = TypeDescriptor.GetConverter (typeof (bool)) as BooleanConverter;
			foreach (XElement el in tag.AllDescendentElements) {
				if (el.Name.Name.ToLower () != "listitem")
					continue;

				// getting all the set properties of the element and the innerHTML
				text = value = innerHtml = String.Empty;
				textPropery = valuePropery = String.Empty;
				selected = false;
				enabled = true;

				// check the attributes
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

				// and get the innerHTML if it's not a selfclosing tag
				if (!el.IsSelfClosing)
					innerHtml = GetTextFromEditor (el.Region.End, el.ClosingTag.Region.Begin);

				// the list item has 4 posible ways of setting it's value property
				// depending on it's attributes and innerHTML

				// set the textProperty
				if (!String.IsNullOrEmpty (innerHtml))
					textPropery = innerHtml;
				else if (!String.IsNullOrEmpty (text))
					textPropery = text;
				else if (!String.IsNullOrEmpty (value))
					textPropery = value;

				// set the valueProperty
				if (!String.IsNullOrEmpty (value))
					valuePropery = value;
				else if (!String.IsNullOrEmpty (innerHtml))
					valuePropery = innerHtml;
				else if (!String.IsNullOrEmpty (text))
					valuePropery = text;

				// create the ListItem with the text, value and enabled properties
				ListItem li = new ListItem (textPropery, valuePropery, enabled);
				// set the item as the selected one if it's a selected attribute
				li.Selected = selected;
				// and add them to the component's items list
				lControl.Items.Add (li);
			}
		}

		/// <summary>
		/// Dict with the attribute names as key and their Type as value
		/// </summary>
		static Dictionary<string, Type> htmlControlTags = new Dictionary<string, Type> () {
			{"a", typeof (HtmlAnchor)},
			{"button", typeof (HtmlButton)},
			{"input", null}, // we'll check that one in the ProcessHtmlControl, because for this tag we have a lot of possible types depending on the type attribute
			{"img", typeof (HtmlImage)},
			{"select", typeof (HtmlSelect)},
			{"textarea", typeof (HtmlTextArea)}
		};

		/// <summary>
		/// Gets the type of the html control.
		/// </summary>
		/// <returns>
		/// The html control type.
		/// </returns>
		/// <param name='el'>
		/// The supposed html control's tag XElement
		/// </param>
		private Type GetHtmlControlType (XElement el)
		{
			// query the htmlControlTags dict for the type
			string nameLowered = el.Name.Name.ToLower ();
			if (!htmlControlTags.ContainsKey (nameLowered))
				return null;

			Type compType = htmlControlTags[nameLowered];
			// for the input tag we have different types depending on the type attribute
			// so in the dict its type is marked with null
			if (compType == null) {
				string typeAttr = XDocumentHelper.GetAttributeValueCI (el.Attributes, "type");
				// get the Type depending on the type attribute
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
		/// Persist the container's controls list matches the tag's attributes' state
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
						// the tag does not have a matching component in the
						// container so create one
						comp = ProcessControl (element);

						if (comp == null)
							continue;

						// assuming that we have an id already from the initial controls parse
						host.Container.Add (comp, id);

						// no need to check for defaults as we have a new component
						checkDefaults = false;
					} else {
						// check if the tag's attributes and the component's attributes match
						checkDefaults = true;
					}

					ProcessControlProperties (element, comp, checkDefaults);
				}
			}
		}

		#endregion
		
		#region Designer communication

		/// <summary>
		/// fired on changing the text in the TextEditor
		/// </summary>
		public event EventHandler Changing;
		/// <summary>
		/// fired when applying changes to the text is finished
		/// </summary>
		public event EventHandler Changed;
		/// <summary>
		/// Occurs when a undo or redo action in the TextEditor was finished
		/// </summary>
		public event EventHandler UndoRedo;

		public void OnChanged ()
		{
			// check if should really fire that event i.e. - variables flag for
			// suppressing the serialization which is subscribed to that event
			// or no changes were made to the text document
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

		/// <summary>
		/// Gets text string from the textEditor between the provided TextLocations.
		/// </summary>
		/// <returns>
		/// The text from editor.
		/// </returns>
		/// <param name='start'>
		/// Start.
		/// </param>
		/// <param name='end'>
		/// End.
		/// </param>
		public string GetTextFromEditor (TextLocation start, TextLocation end)
		{
			return GetTextFromEditor (start.Line, start.Column, end.Line, end.Column);
		}

		/// <summary>
		/// Gets text string from the textEditor between the provided text coordinates.
		/// </summary>
		/// <returns>
		/// The text from editor.
		/// </returns>
		/// <param name='startLine'>
		/// Start line.
		/// </param>
		/// <param name='startColumn'>
		/// Start column.
		/// </param>
		/// <param name='endLine'>
		/// End line.
		/// </param>
		/// <param name='endColumn'>
		/// End column.
		/// </param>
		public string GetTextFromEditor (int startLine, int startColumn, int endLine, int endColumn)
		{
			if (textEditor == null)
				throw new NullReferenceException ("The SourceEditorView is not set. Can't process document for text nodes.");

			return textEditor.GetTextBetween (startLine, startColumn, endLine, endColumn);
		}

		/// <summary>
		/// Replaces a text string in the editor with the newValue in the provided region
		/// </summary>
		/// <param name='region'>
		/// Region.
		/// </param>
		/// <param name='newValue'>
		/// New value.
		/// </param>
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

			// let the undo tracker know that an action was finished in the source editor
			undoTracker.FinishAction ();

			// let the parser know that the content is dirty and set the event
			TxtDocDirty = true;
			updateEditorContent.Set ();
		}

		/// <summary>
		/// Removes the text in the provided DomRegion
		/// </summary>
		/// <param name='region'>
		/// Region.
		/// </param>
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

		/// <summary>
		/// Inserts a string in the sourcecode editor at TextLocation loc.
		/// </summary>
		/// <param name='loc'>
		/// Location.
		/// </param>
		/// <param name='text'>
		/// Text.
		/// </param>
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

		/// <summary>
		/// Perform an undo in the sourcecode editor.
		/// </summary>
		public void Undo ()
		{
			if (undoTracker.CanUndo) {
				undoHandler.Undo ();
				TxtDocDirty = true;
				undoTracker.UndoAction ();
				OnUndoRedo ();
			}
		}

		/// <summary>
		/// Perform an redo in the sourcecode editor.
		/// </summary>
		public void Redo ()
		{
			if (undoTracker.CanRedo) {
				undoHandler.Redo ();
				TxtDocDirty = true;
				undoTracker.RedoAction ();
				OnUndoRedo ();
			}
		}

		/// <summary>
		/// Queries the undo tracker if undo can be performed
		/// </summary>
		/// <returns>
		/// <c>true</c> if we can perform an undo in the source editor; otherwise, <c>false</c>.
		/// </returns>
		public bool CanUndo ()
		{
			return undoTracker.CanUndo;
		}

		/// <summary>
		/// Queries the undo tracker if redo can be performed
		/// </summary>
		/// <returns>
		/// <c>true</c> if we can perform an redo in the source editor; otherwise, <c>false</c>.
		/// </returns>
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
