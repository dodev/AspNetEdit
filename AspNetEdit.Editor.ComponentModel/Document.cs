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
		int directivePlaceHolderKey = 0;

		private Control parent;
		private DesignerHost host;

		AspNetParser parser;
		AspNetParsedDocument aspNetDoc;
		ExtensibleTextEditor textEditor;
		bool txtDocDirty; // notes when the content of the textEditor doesn't match the content of the XDocument
		string designableHtml = String.Empty;
		string designerContext = String.Empty;
		
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
			InitDesignerContext ();
		}

		public void InitDesignerContext ()
		{
			string scriptTag = "\n<script type=\"text/javascript\" src=\"{0}\"></script>";
			string cssLinkTag = "\n<link rel=\"stylesheet\" type=\"text/css\" href=\"{0}\" />";

			string scriptDir = "js";
			string[] scripts = {
				"jquery-1.7.2.min.js",
				"main.js",
				"handlers.js"
			};
			string styleDir = "css";
			string[] styleSheets = {
				"control_style.css"
			};

			designerContext = String.Empty;
			foreach (string script in scripts)
				designerContext += String.Format (scriptTag, Path.Combine (scriptDir, script));
			foreach (string styleFile in styleSheets)
				designerContext += String.Format (cssLinkTag, Path.Combine (styleDir, styleFile));

			designerContext += "\n";
		}

		public void PersistDocument ()
		{
			System.Threading.Thread worker = new System.Threading.Thread (new System.Threading.ThreadStart(StartPersistingDocument));
			worker.Start ();
		}

		public void StartPersistingDocument ()
		{
			OnChanging ();
			try {
				// parse the contents of the textEditor
				Parse ();
	
				// initializing the dicts of directives and controls tags
				if (directives == null) {
					directives = new Hashtable (StringComparer.InvariantCultureIgnoreCase);
					CheckForDirective (aspNetDoc.XDocument.AllDescendentNodes);
					ParseControls ();
				}
	
				// serialize the tree to designable HTML
				designableHtml = serializeNode (aspNetDoc.XDocument.RootElement);
			} catch (Exception ex) {
				System.Diagnostics.Trace.WriteLine (ex.ToString ());
			}
			OnChanged ();
		}


		#region StateEngine parser

		/// <summary>
		/// Parse the TextEditor.Text document and tracks the txtDocDirty flag.
		/// </summary>
		AspNetParsedDocument Parse ()
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

			if (element.Name.HasPrefix || IsRunAtServer (element)) {
				string id = GetAttributeValueCI (element.Attributes, "id");

				try {
					// check the DesignContainer if a component for that node already exists
					if (string.IsNullOrEmpty(id) || (host.GetComponent(id) == null)) {
						IComponent comp = ProcessControl (element);
						if (comp != null) {
							this.host.Container.Add (comp, id);
	
							// add id to the component, for later recognition
							if (id == string.Empty) {
								InsertAttribute (element, "id", comp.Site.Name);
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

		// adds an attribute to the end of the openning  tag
		void InsertAttribute (XElement el, string key, string value)
		{
			int line = el.Region.EndLine;
			int column = 1;
			string preambula = string.Empty;
			string ending = string.Empty;

			if (el.IsSelfClosing) {
				column = el.Region.EndColumn - 2; // "/>"
				ending = " ";
			} else {
				column = el.Region.EndColumn -1; // ">"
			}

			if (column > 1) {
				string whatsBeforeUs = textEditor.GetTextBetween (line, column - 1, line, column);
				if (!string.IsNullOrWhiteSpace (whatsBeforeUs))
					preambula = " ";
			}
			Gtk.Application.Invoke (delegate {
				textEditor.SetCaretTo (line, column);
				textEditor.InsertAtCaret (string.Format ("{0}{1}=\"{2}\"{3}", preambula, key, value, ending));
			});

			txtDocDirty = true;
		}

		void UpdateAttribute (XAttribute attr, string newValue)
		{
			Gtk.Application.Invoke (delegate {
				textEditor.Remove (attr.Region);
				textEditor.SetCaretTo (attr.Region.BeginLine, attr.Region.BeginColumn);
				textEditor.InsertAtCaret (String.Format ("{0}=\"{1}\"", attr.Name.Name, newValue));
			});
			txtDocDirty = true;
		}

		public void UpdateTag (string id, Control updatedControl)
		{
			try {
				Dictionary <string, string> properties = new Dictionary<string, string> (StringComparer.InvariantCultureIgnoreCase);			// we need the type to get the properties
				System.Type controlType = updatedControl.GetType ();
	
				// filter the properties to get the changed ones
				var collection = TypeDescriptor.GetProperties (controlType, new Attribute[] {BrowsableAttribute.Yes}) as PropertyDescriptorCollection;
	
				foreach (PropertyDescriptor desc in collection) {
					try {
					var defVal = desc.Attributes[typeof (DefaultValueAttribute)] as DefaultValueAttribute;

					if ((defVal != null) && !desc.GetValue(updatedControl).Equals(defVal.Value)) {
						
						// workaround for a bug in System.Web.UI.Control
						// the DefaultValueAttribute is set to a string - "0", while it should be of enum ViewStateMode
						// and so that the ViewStateMode property appears to always have a value different from the default
						if (desc.Name == "ViewStateMode") {
							var val = (ViewStateMode)desc.GetValue (updatedControl);
							if (val == ViewStateMode.Inherit) // ViewStateMode.Inherit is the default
								continue;
						}

						var converter = TypeDescriptor.GetConverter (desc.PropertyType) as TypeConverter;
						if (converter != null) {
							properties.Add (desc.Name, converter.ConvertToString (desc.GetValue (updatedControl)));
						} else
							properties.Add (desc.Name, desc.GetValue (updatedControl).ToString ());
					}
					} catch (Exception ex) {
						// something
						System.Diagnostics.Trace.WriteLine (ex.ToString ());
					}
				}
	
				// get the tag node
				AspNetParsedDocument doc = Parse ();
				XElement el = GetControlTag (doc.XDocument.RootElement, id);
				if (el == null)
					throw new Exception ("Could not find element with id = " + id);
	
				// if the id was changed. i.e. is in the filtered properties
				if (properties.ContainsKey("id")) {
					if (host.GetComponent (properties["id"]) != null)
						throw new Exception ("Element with that name already excists: "); // TODO: display warning instead of exception
	
					// update the key of the component in the IContainer
					host.Container.Remove (updatedControl);
					host.Container.Add (updatedControl, properties["id"]);
				}
	
				// add to the properies array all the attributes that weren't changed
				foreach (XAttribute attr in el.Attributes) {
					if (!properties.ContainsKey (attr.Name.Name)) 
						properties[attr.Name.Name] = attr.Value;
				}
	
				// build the new node
				string attributes = string.Empty;
	
				foreach (KeyValuePair<string, string> kv in properties)
					attributes += string.Format (" {0}=\"{1}\"", kv.Key, kv.Value);
	
				string newTag = string.Format ("<{0}{1}{2}>", el.Name.FullName, attributes, el.IsSelfClosing ? " /" : "");
	
				// and replace the node in the TextEditor
				textEditor.Remove (el.Region);
				textEditor.SetCaretTo (el.Region.BeginLine, el.Region.BeginColumn);
				textEditor.InsertAtCaret (newTag);
			} catch (Exception ex) {
				System.Diagnostics.Trace.WriteLine (ex.ToString ());
			}

			// update the document's representation
			txtDocDirty = true;
			PersistDocument ();
		}

		public void UpdateTag (IComponent component, MemberDescriptor memberDesc, object newVal)
		{
			string key = String.Empty;
			string value = String.Empty;
			AspNetParsedDocument doc = Parse ();
			XElement el = GetControlTag (doc.XDocument.RootElement, component.Site.Name);

			if (memberDesc is PropertyDescriptor) {
				var propDesc = memberDesc as PropertyDescriptor;
				key = memberDesc.Name;
				value = propDesc.Converter.ConvertToString (newVal);
			} else if (memberDesc is EventDescriptor) {
				//var eventDesc = memberDesc as EventDescriptor;
				//key = "On" + eventDesc.Name;
				// TODO: get the handler method name
				//value = newVal.ToString ();
			} else {
				// well, well, well! what do we have here!
			}

			bool found = false;

			// check if the changed attribute was already in the tag 
			foreach (XAttribute attr in el.Attributes) {
				if (attr.Name.Name.ToLower () == key.ToLower ()) {
					UpdateAttribute (attr, value);
					found = true;
					break;
				}
			}

			// if it was not in the tag, add it
			if (!found) {
				InsertAttribute (el, key, value);
			}

			txtDocDirty = true;
			PersistDocument ();
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
					string currId = GetAttributeValueCI (el.Attributes, "id");
					if (IsRunAtServer (el) && (string.Compare(currId, id, true) == 0)) {
						controlTag = el;
						break;
					}
					controlTag = GetControlTag (el, id);
				} 
			}
			return controlTag;
		}

		#endregion
		
		#region Designer communication

		public string ToDesignTimeHtml ()
		{
			// serialize everything insed the <html> tag
			return designableHtml;
		}

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
		
		#region Serialization
		
		TextLocation prevTagLocation = new TextLocation (TextLocation.MinLine, TextLocation.MinColumn);

		string serializeNode (MonoDevelop.Xml.StateEngine.XNode node)
		{
			prevTagLocation = node.Region.End;

			var element = node as XElement;

			if (element == null) {

				return string.Empty;
			}

			string id = GetAttributeValueCI (element.Attributes, "id");

			// Controls are runat="server" and have unique id in the Container
			if (element.Name.HasPrefix || IsRunAtServer (element)) {
				IComponent component = host.GetComponent (id);

				// HTML controls, doesn't need special rendering
				var control = component as Control;

				// genarete placeholder
				if (control != null) {
					string content = "<div class=\"aspnetedit_control_container\">";
					StringWriter strWriter = new StringWriter ();
					HtmlTextWriter writer = new HtmlTextWriter (strWriter);

					control.RenderControl (writer);
					writer.Close ();
					strWriter.Flush ();
					content += strWriter.ToString ();
					content += "</div>";
					strWriter.Close ();
					if (!element.IsSelfClosing)
						prevTagLocation = element.ClosingTag.Region.End;
					return content;
				}
			}

			// strip script tags
			if (element.Name.Name.ToLower () == "script")
				return string.Empty;

			// the node is a html element
			string output = "<" + element.Name.FullName;
			
			// print the attributes
			foreach (MonoDevelop.Xml.StateEngine.XAttribute attr in element.Attributes) {
				string name = attr.Name.Name.ToLower ();
				// strip runat and on* event attributes
				if ((name != "runat") && (name.Substring (0, 2).ToLower () != "on"))
					output += " " + attr.Name.FullName + "=\"" + attr.Value + "\"";
			}
			
			if (element.IsSelfClosing) {
				output += " />";
			} else {
				output += ">";

				// we are currentyl on the head tag
				// add designer content - js and css
				if (element.Name.Name.ToString () == "head") {
					output += designerContext;
				}
				
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
			if (textEditor == null)
				throw new NullReferenceException ("The SourceEditorView is not set. Can't process document for text nodes.");

			return textEditor.GetTextBetween (start.Line, start.Column, end.Line, end.Column);
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
	}
}
