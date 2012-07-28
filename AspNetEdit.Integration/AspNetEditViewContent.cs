//
// AspNetEditViewContent.cs: The SecondaryViewContent that lets AspNetEdit 
//         be used as a designer in MD.
//
// Authors:
//   Michael Hutchinson <m.j.hutchinson@gmail.com>
//
// Copyright (C) 2006 Michael Hutchinson
//
//
// This source code is licenced under The MIT License:
//
// Permission is hereby granted, free of charge, to any person obtaining
// a copy of this software and associated documentation files (the
// "Software"), to deal in the Software without restriction, including
// without limitation the rights to use, copy, modify, merge, publish,
// distribute, sublicense, and/or sell copies of the Software, and to
// permit persons to whom the Software is furnished to do so, subject to
// the following conditions:
// 
// The above copyright notice and this permission notice shall be
// included in all copies or substantial portions of the Software.
// 
// THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND,
// EXPRESS OR IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF
// MERCHANTABILITY, FITNESS FOR A PARTICULAR PURPOSE AND
// NONINFRINGEMENT. IN NO EVENT SHALL THE AUTHORS OR COPYRIGHT HOLDERS BE
// LIABLE FOR ANY CLAIM, DAMAGES OR OTHER LIABILITY, WHETHER IN AN ACTION
// OF CONTRACT, TORT OR OTHERWISE, ARISING FROM, OUT OF OR IN CONNECTION
// WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE SOFTWARE.
//

using System;
using System.IO;
using System.ComponentModel;
using System.ComponentModel.Design;
using Gtk;

using Mono.Addins;
using MonoDevelop.AspNet.Parser;
using MonoDevelop.Ide.Gui;
using MonoDevelop.Core;
using MonoDevelop.Ide;
using MonoDevelop.Core.Execution;
using MonoDevelop.DesignerSupport.Toolbox;
using MonoDevelop.DesignerSupport;
using MonoDevelop.Components.PropertyGrid;
using MonoDevelop.SourceEditor;
using MonoDevelop.Xml.StateEngine;

using AspNetEdit.Editor;
using AspNetEdit.Editor.ComponentModel;

namespace AspNetEdit.Integration
{
	
	public class AspNetEditViewContent : AbstractAttachableViewContent, IToolboxConsumer, IOutlinedDocument, IPropertyPadProvider //, IEditableTextBuffer
	{
		IViewContent viewContent;
		EditorProcess editorProcess;
		
		Gtk.Socket designerSocket;
//		Gtk.Socket propGridSocket;
		
//		Frame propertyFrame;
		Gtk.Frame designerFrame;
		
		MonoDevelop.Ide.Gui.Components.PadTreeView outlineView;
		Gtk.TreeStore outlineStore;
		
		MonoDevelopProxy proxy;
		
		bool activated = false;
		bool suppressSerialisation = false;
		
		internal AspNetEditViewContent (IViewContent viewContent)
		{
			this.viewContent = viewContent;
			
			designerFrame = new Frame ();
			designerFrame.CanFocus = true;
			designerFrame.Shadow = ShadowType.None;
			designerFrame.BorderWidth = 0;
			
//			propertyFrame = new Frame ();
//			propertyFrame.CanFocus = true;
//			propertyFrame.Shadow = ShadowType.None;
//			propertyFrame.BorderWidth = 0;
			
			viewContent.WorkbenchWindow.Closing += workbenchWindowClosingHandler;
			
			outlineStore = null;
			outlineStore = null;
			
			designerFrame.Show ();
		}
		
		void workbenchWindowClosingHandler (object sender, WorkbenchWindowEventArgs args)
		{
			if (activated)
				suppressSerialisation = true;
		}
		
		public override Gtk.Widget Control {
			get { return designerFrame; }
		}
		
		public override string TabPageLabel {
			get { return "Designer"; }
		}
		
		bool disposed = false;
		
		public override void Dispose ()
		{
			if (disposed)
				return;
			
			disposed = true;
			
			base.WorkbenchWindow.Closing -= workbenchWindowClosingHandler;
			
			DestroyEditorAndSockets ();
			designerFrame.Destroy ();
			base.Dispose ();
		}
		
		public override void Selected ()
		{
			if (editorProcess != null)
				throw new Exception ("Editor should be null when document is selected");
			
			designerSocket = new Gtk.Socket ();
			designerSocket.Show ();
			designerFrame.Add (designerSocket);
			
//			propGridSocket = new Gtk.Socket ();
//			propGridSocket.Show ();
//			propertyFrame.Add (propGridSocket);
			
			// FIXME: Runtime.ProcessService cannot load EditorProcess from AspNetEdit assembly
			//editorProcess = (EditorProcess)Runtime.ProcessService.CreateExternalProcessObject (typeof(EditorProcess), false);
			editorProcess = new EditorProcess ();
			
			if (designerSocket.IsRealized)
				editorProcess.AttachDesigner (designerSocket.Id);
//			if (propGridSocket.IsRealized)
//				editorProcess.AttachPropertyGrid (propGridSocket.Id);
			
			designerSocket.Realized += delegate {
				editorProcess.AttachDesigner (designerSocket.Id);
			};
//			propGridSocket.Realized += delegate {
//				editorProcess.AttachPropertyGrid (propGridSocket.Id);
//			};
			
			//designerSocket.FocusOutEvent += delegate {
			//	MonoDevelop.DesignerSupport.DesignerSupport.Service.PropertyPad.BlankPad (); };

			SourceEditorView srcEditor = viewContent.GetContent<SourceEditorView> () as SourceEditorView;
			AspNetParsedDocument doc = null;
			
			//hook up proxy for event binding
			string codeBehind = null;
			if (viewContent.Project != null) {
				using (StringReader reader = new StringReader (srcEditor.Text)) {
					AspNetParser parser = new AspNetParser ();
					doc = parser.Parse (true, viewContent.ContentName, reader, viewContent.Project)
						as AspNetParsedDocument;
					
					if (doc != null && doc.Info != null) {
						if (string.IsNullOrEmpty (doc.Info.InheritedClass))
							codeBehind = doc.Info.InheritedClass;
					}
				}
			}
			proxy = new MonoDevelopProxy (viewContent.Project, codeBehind);
			
			editorProcess.Initialise (proxy, srcEditor.TextEditor, doc);
			
			activated = true;

			// TODO: update the tree on changes in the Dom
			BuildTreeStore (doc.XDocument);
		}
		
		public override void Deselected ()
		{
			activated = false;
			
			//don't need to save if window is closing
			if (!suppressSerialisation)
				saveDocumentToTextView ();
			
			DestroyEditorAndSockets ();
		}
			
		void saveDocumentToTextView ()
		{
			if (editorProcess != null && !editorProcess.ExceptionOccurred) {
				/* TODO: Reimplement the Editor.GetDocument method
				IEditableTextBuffer textBuf = (IEditableTextBuffer) viewContent.GetContent<IEditableTextBuffer> ();
				
				string doc = null;
				try {
					doc = editorProcess.Editor.GetDocument ();
				} catch (Exception e) {
					MonoDevelop.Ide.MessageService.ShowException (e,
						AddinManager.CurrentLocalizer.GetString (
					        "The document could not be retrieved from the designer"));
				}
			
				if (doc != null)
					textBuf.Text = doc;
					
				*/
			}
		}
		
		void DestroyEditorAndSockets ()
		{
			// FIXME: dispose the EditorProcess and the sockets
			editorProcess = null;
//			if (proxy != null) {
//				proxy.Dispose ();
//				proxy = null;
//			}
//			
//			if (editorProcess != null) {
//				editorProcess.Dispose ();
//				editorProcess = null;
//			}
//			
//			if (propGridSocket != null) {
//				propertyFrame.Remove (propGridSocket);
//				propGridSocket.Dispose ();
//				propGridSocket = null;
//			}
//			
//			if (designerSocket != null) {
//				designerFrame.Remove (designerSocket);
//				designerSocket.Dispose ();
//				designerSocket = null;
//			}
		}
		
		#region IToolboxConsumer
		
		public void ConsumeItem (ItemToolboxNode node)
		{
			if (node is ToolboxItemToolboxNode)
				editorProcess.Editor.UseToolboxNode (node);
		}
		
		//used to filter toolbox items
		private static ToolboxItemFilterAttribute[] atts = new ToolboxItemFilterAttribute[] {
			new System.ComponentModel.ToolboxItemFilterAttribute ("System.Web.UI", ToolboxItemFilterType.Allow)
		};
			
		public ToolboxItemFilterAttribute[] ToolboxFilterAttributes {
			get { return atts; }
		}
		
		public System.Collections.Generic.IList<ItemToolboxNode> GetDynamicItems ()
		{
			return null;
		}
		
		//Used if ToolboxItemFilterAttribute demands ToolboxItemFilterType.Custom
		//If not expecting it, should just return false
		public bool CustomFilterSupports (ItemToolboxNode item)
		{
			return false;
		}
		
		public void DragItem (ItemToolboxNode item, Widget source, Gdk.DragContext ctx)
		{
		}
		
		public TargetEntry[] DragTargets {
			get { return null; }
		}
		
		string IToolboxConsumer.DefaultItemDomain {
			get { return null; }
		}

		#endregion IToolboxConsumer
		
		#region DocumentOutline stuff
		
		#region IOutlinedDocument implementation
		
		Widget IOutlinedDocument.GetOutlineWidget ()
		{
			if (outlineView != null)
				return outlineView;
				
			if (outlineStore == null)
				throw new Exception ("The treestore should be built, before initializing the TreeView of the DocumentOutline");
			
			outlineView = new MonoDevelop.Ide.Gui.Components.PadTreeView (outlineStore);
			System.Reflection.PropertyInfo prop = typeof(Gtk.TreeView).GetProperty ("EnableTreeLines");
			if (prop != null)
				prop.SetValue (outlineView, true, null);
			outlineView.TextRenderer.Xpad = 0;
			outlineView.TextRenderer.Ypad = 0;
			outlineView.ExpandAll ();
			outlineView.AppendColumn ("Node", outlineView.TextRenderer, new Gtk.TreeCellDataFunc (OutlineTreeDataFunc));
			outlineView.HeadersVisible = false;
			outlineView.Selection.Changed += delegate {
				Gtk.TreeIter iter = Gtk.TreeIter.Zero;
				outlineView.Selection.GetSelected (out iter);
				DocumentOutlineSelectionChanged (outlineStore.GetValue (iter, 0) as XNode);
			};
			
			var sw = new MonoDevelop.Components.CompactScrolledWindow ();
			sw.Add (outlineView);
			sw.ShowAll ();
				
			return sw;
		}

		System.Collections.Generic.IEnumerable<Widget> IOutlinedDocument.GetToolbarWidgets ()
		{
			return null;
		}

		void IOutlinedDocument.ReleaseOutlineWidget ()
		{
			if (outlineView != null) {
				Gtk.ScrolledWindow w = (Gtk.ScrolledWindow)outlineView.Parent;
				w.Destroy ();
				outlineView.Destroy ();
				outlineView = null;
			}
			
			if (outlineStore != null) {
				outlineStore.Dispose ();
				outlineStore = null;
			}
		}
		
		#endregion IOutlinedDocument implementation
		
		void BuildTreeStore (XDocument doc)
		{
			outlineStore = new TreeStore (typeof (object));
			BuildTreeChildren (Gtk.TreeIter.Zero, doc);
		}
		
		void BuildTreeChildren (Gtk.TreeIter parent, XContainer p)
		{
			foreach (XNode n in p.Nodes) {
				Gtk.TreeIter childIter;
				if (!parent.Equals (Gtk.TreeIter.Zero))
					childIter = outlineStore.AppendValues (parent, n);
				else
					childIter = outlineStore.AppendValues (n);
				
				XContainer c = n as XContainer;
				if (c != null && c.FirstChild != null)
					BuildTreeChildren (childIter, c);
			}
		}
		
		void OutlineTreeDataFunc (Gtk.TreeViewColumn column, Gtk.CellRenderer cell, Gtk.TreeModel model, Gtk.TreeIter iter)
		{
			Gtk.CellRendererText txtRenderer = (Gtk.CellRendererText) cell;
			XNode n = (XNode) model.GetValue (iter, 0);
			txtRenderer.Text = n.FriendlyPathRepresentation;
		}
		
		void DocumentOutlineSelectionChanged (MonoDevelop.Xml.StateEngine.XNode selNode)
		{
			if (selNode == null)
				return; // not what we are looking for
			
			XElement el = selNode as XElement;
			
			if (el == null)
				return; // not a XML tag node
			
			bool isRunAtServer = false;
			string id = string.Empty;
			XName runatName = new XName ("runat");
			XName idName = new XName ("id");
			
			foreach (XAttribute attr in el.Attributes) {
				if (attr.Name.ToLower () == runatName) {
					if (attr.Value == "server")
						isRunAtServer = true;
					else
						break;
				} else if (attr.Name.ToLower () == idName) {
					id = attr.Value;
				}
			}
			
			if (isRunAtServer && (id != string.Empty) && (editorProcess != null)) {

				// TODO: Add a unique field to editable nodes. the id of the node is not guaranteed to be the component's Site.Name
				IComponent selected = editorProcess.Editor.DesignerHost.GetComponent (id);

				if (selected != null) {
					var properties = TypeDescriptor.GetProperties (selected) as PropertyDescriptorCollection;

					var selServ = editorProcess.Editor.Services.GetService (typeof (ISelectionService)) as ISelectionService;
					selServ.SetSelectedComponents (new IComponent[] {selected});
				}
			}
		}
		
		#endregion DocumentOutline stuff

		#region IPropertyPadProvider implementation
		/// <summary>
		/// The edited component identifier. Stores the id of the component in case it is changed in the property pad.
		/// </summary>
		string editedComponentId = string.Empty;

		public object GetActiveComponent ()
		{
			var selServ = editorProcess.Editor.Services.GetService (typeof (ISelectionService)) as ISelectionService;
			if (selServ == null)
				return null;

			editedComponentId = (selServ.PrimarySelection as IComponent).Site.Name;

			return selServ.PrimarySelection;
		}

		public object GetProvider ()
		{
			return GetActiveComponent ();
		}

		public void OnEndEditing (object obj)
		{
		}

		public void OnChanged (object obj)
		{

		}
		#endregion
		
//		class DesignerFrame: Frame//, ICustomPropertyPadProvider
//		{
//			AspNetEditViewContent view;
//			
//			public DesignerFrame (AspNetEditViewContent view)
//			{
//				this.view = view;
//			}
			
//			Gtk.Widget ICustomPropertyPadProvider.GetCustomPropertyWidget ()
//			{
//				return view.propertyFrame;
//			}
//			
//			void ICustomPropertyPadProvider.DisposeCustomPropertyWidget ()
//			{
//			}
//		}
	}
}
