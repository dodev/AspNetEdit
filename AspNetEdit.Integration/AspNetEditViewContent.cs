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
using MonoDevelop.DesignerSupport.Toolbox;
using MonoDevelop.DesignerSupport;
using MonoDevelop.Components.PropertyGrid;
using MonoDevelop.SourceEditor;
using MonoDevelop.Xml.StateEngine;

using AspNetEdit.Editor;
using AspNetEdit.Editor.ComponentModel;
using MonoDevelop.Components.Commands;
using MonoDevelop.Ide.Commands;
using MonoDevelop.Ide.Gui.Content;

namespace AspNetEdit.Integration
{
	
	public class AspNetEditViewContent : AbstractAttachableViewContent, IToolboxConsumer, IOutlinedDocument, IPropertyPadProvider //, IEditableTextBuffer
	{
		IViewContent viewContent;
		EditorHost host;

		Frame designerFrame;
		ScrolledWindow webKitWindow;
		
		MonoDevelop.Ide.Gui.Components.PadTreeView outlineView;
		Gtk.TreeStore outlineStore;
		
		MonoDevelopProxy proxy;
		
		bool activated = false;
		bool blockSelected = false;
		
		internal AspNetEditViewContent (IViewContent viewContent)
		{
			this.viewContent = viewContent;

			designerFrame = new Frame ();
			designerFrame.CanFocus = true;
			designerFrame.Shadow = ShadowType.Out;
			designerFrame.BorderWidth = 1;
			
			viewContent.WorkbenchWindow.Closing += new WorkbenchWindowEventHandler(workbenchWindowClosingHandler);
			
			outlineStore = null;
			outlineView = null;
			
			designerFrame.Show ();
		}

		void workbenchWindowClosingHandler (object sender, WorkbenchWindowEventArgs args)
		{
			if (!disposed)
				Dispose ();
			blockSelected = true;
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

		bool IsInCurrentViewContent ()
		{
			return IdeApp.Workbench.ActiveDocument.ActiveView.Equals (this as IBaseViewContent);
		}

		public override void Selected ()
		{
			if (blockSelected || !IsInCurrentViewContent ())
				return;

			if (activated)
				throw new Exception ("Editor should be null when document is selected");

			var doc = IdeApp.Workbench.ActiveDocument.ParsedDocument as AspNetParsedDocument;
			if (doc != null) {
				proxy = new MonoDevelopProxy (viewContent.Project, doc.Info.InheritedClass);
				System.Diagnostics.Trace.WriteLine ("Creating AspNetEdit EditorHost");
				host = new EditorHost (proxy);
				host.Initialise ();
				System.Diagnostics.Trace.WriteLine ("Created AspNetEdit EditorHost");
				activated = true;

				// Loading the GUI of the Designer
				LoadGui ();

				// Loading the doc structure in the DocumentOutlinePad
				BuildTreeStore (doc.XDocument);
				// subscribing to changes in the DOM
				IdeApp.Workbench.ActiveDocument.DocumentParsed += document_OnParsed;
			}
		}
		
		public override void Deselected ()
		{
			activated = false;			
			DestroyEditorAndSockets ();
		}

		void LoadGui ()
		{
			System.Diagnostics.Trace.WriteLine ("Building AspNetEdit GUI");

			webKitWindow = new ScrolledWindow ();
			webKitWindow.Add (host.DesignerView);
			webKitWindow.ShowAll ();
			designerFrame.Add (webKitWindow);
			System.Diagnostics.Trace.WriteLine ("Built AspNetEdit GUI");
		}

		void DestroyEditorAndSockets ()
		{
			if (proxy != null) {
				proxy.Dispose ();
				proxy = null;
			}

			if (host != null) {
				System.Diagnostics.Trace.WriteLine ("Disposing AspNetEdit's EditorHost");

				designerFrame.Remove (webKitWindow);
				webKitWindow.Dispose ();
				host.Dispose ();
				host = null;

				System.Diagnostics.Trace.WriteLine ("Disposed AspNetEdit's EditorHost");
			}

			if (IdeApp.Workbench.ActiveDocument != null) {
				IdeApp.Workbench.ActiveDocument.DocumentParsed -= document_OnParsed;
			}
		}
		
		#region IToolboxConsumer
		
		public void ConsumeItem (ItemToolboxNode node)
		{
			if (node is ToolboxItemToolboxNode)
				host.UseToolboxNode (node);
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

		void document_OnParsed (object o, EventArgs args)
		{
			BuildTreeStore ((IdeApp.Workbench.ActiveDocument.ParsedDocument as AspNetParsedDocument).XDocument);
		}

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
			
			if (isRunAtServer && (id != string.Empty) && (host != null)) {

				// TODO: Add a unique field to editable nodes. the id of the node is not guaranteed to be the component's Site.Name
				IComponent selected = host.DesignerHost.GetComponent (id);

				if (selected != null) {
					//var properties = TypeDescriptor.GetProperties (selected) as PropertyDescriptorCollection;

					var selServ = host.Services.GetService (typeof (ISelectionService)) as ISelectionService;
					selServ.SetSelectedComponents (new IComponent[] {selected});
				}
			}
		}
		
		#endregion DocumentOutline stuff

		#region Editor command

		[CommandHandler (EditCommands.Delete)]
		protected void OnDelete ()
		{
			host.DesignerHost.RemoveSelectedControls ();
		}
		
		[CommandUpdateHandler (EditCommands.Delete)]
		protected void OnUpdateDelete (CommandInfo info)
		{
			var selServ = host.Services.GetService (typeof (ISelectionService)) as ISelectionService;
			info.Enabled = selServ != null && selServ.SelectionCount > 0;
		}

		[CommandHandler (EditCommands.Undo)]
		protected void OnUndo ()
		{
			host.DesignerHost.RootDocument.Undo ();
		}
		
		[CommandUpdateHandler (EditCommands.Undo)]
		protected void OnUpdateUndo (CommandInfo info)
		{
			info.Enabled = host != null && host.DesignerHost.RootDocument.CanUndo ();
		}
		
		[CommandHandler (EditCommands.Redo)]
		protected void OnRedo ()
		{
			host.DesignerHost.RootDocument.Redo ();
		}
		
		[CommandUpdateHandler (EditCommands.Redo)]
		protected void OnUpdateRedo (CommandInfo info)
		{
			info.Enabled = host != null && host.DesignerHost.RootDocument.CanRedo ();
		}

		#endregion Editor command

		#region IPropertyPadProvider implementation

		public object GetActiveComponent ()
		{
			var selServ = host.Services.GetService (typeof (ISelectionService)) as ISelectionService;
			if (selServ == null)
				return null;

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
	}
}
