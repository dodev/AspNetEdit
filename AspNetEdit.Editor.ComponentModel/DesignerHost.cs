/* 
* DesignerHost.cs - IDesignerHost implementation. Designer transactions
*  and service host. One level up from DesignContainer, tracks RootComponent. 
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
using System.ComponentModel;
using System.ComponentModel.Design;
using System.Reflection;
using System.Collections;
using System.Drawing.Design;
using System.IO;
using System.Web.UI;
using System.Web.UI.Design;

using MonoDevelop.SourceEditor;
using MonoDevelop.AspNet;
using MonoDevelop.AspNet.Parser;

namespace AspNetEdit.Editor.ComponentModel
{
	public class DesignerHost : IDesignerHost, IDisposable
	{
		private ServiceContainer parentServices;
		EditorHost editorHost;

		public DesignerHost (ServiceContainer parentServices, EditorHost host)
		{
			this.parentServices = parentServices;
			container = new DesignContainer (this);

			//register services
			parentServices.AddService (typeof (IDesignerHost), this);
			parentServices.AddService (typeof (IComponentChangeService), container);

			editorHost = host;
		}

		#region Component management

		private DesignContainer container;
		private IComponent rootComponent = null;
		private Document rootDocument;
		private DocumentSerializer serializer;
		private DesignerSerializer designerSerializer;

		public IContainer Container
		{
			get { return container; }
		}

		public DesignerSerializer AspNetSerializer {
			get { return designerSerializer; }
		}
		
		public IComponent CreateComponent (Type componentClass, string name)
		{
			//add to document, unless loading
			bool addToDoc = (this.RootDocument != null);
			return CreateComponent (componentClass, name, addToDoc);
		}
		
		internal IComponent CreateComponent (Type componentClass, string name, bool addToDoc)
		{
			System.Diagnostics.Trace.WriteLine("Attempting to create component "+name);
			//check arguments
			if (componentClass == null)
				throw new ArgumentNullException ("componentClass");
			if (!componentClass.IsSubclassOf (typeof (System.Web.UI.Control)) && componentClass != typeof (System.Web.UI.Control))
				throw new ArgumentException ("componentClass must be a subclass of System.Web.UI.Control, but is a " + componentClass.ToString (), "componentClass");

			if (componentClass.IsSubclassOf (typeof (System.Web.UI.Page)))
				throw new InvalidOperationException ("You cannot directly add a page to the host. Use NewFile() instead");

			//create the object
			IComponent component = (IComponent) Activator.CreateInstance (componentClass);

			//and add to container
			container.Add (component, name);
			
			if (addToDoc) {
				((Control)RootComponent).Controls.Add ((Control) component);
				RootDocument.AddControl ((Control)component);
			
				//select it
				ISelectionService sel = this.GetService (typeof (ISelectionService)) as ISelectionService;
				if (sel != null)
					sel.SetSelectedComponents (new IComponent[] {component});
			}
			
			System.Diagnostics.Trace.WriteLine("Created component "+name);
			return component;
		}

		public IComponent CreateComponent (Type componentClass)
		{
			return CreateComponent (componentClass, null);
		}

		public IComponent GetComponent (string name)
		{
			return container.GetComponent (name);
		}

		public void DestroyComponent (IComponent component)
		{
			//deselect it if selected
			ISelectionService sel = this.GetService (typeof (ISelectionService)) as ISelectionService;
			bool found = false;
			if (sel != null)
				foreach (IComponent c in sel.GetSelectedComponents ())
					if (c == component) {
						found = true;
						break;
					}
			//can't modify selection in loop
			if (found) sel.SetSelectedComponents (null);
						
			if (component != RootComponent) {
				//remove from component and document
				((Control) RootComponent).Controls.Remove ((Control) component);
				RootDocument.RemoveControl ((Control)component);
			}

			//remove from container if still sited
			if (component.Site != null)
				container.Remove (component);
			
			component.Dispose ();
		}

		public IDesigner GetDesigner (IComponent component)
		{
			if (component == null)
				throw new ArgumentNullException ("component");
			else
				return container.GetDesigner (component);
		}

		public Type GetType (string typeName)
		{
			//use ITypeResolutionService if we have it, else Type.GetType();
			object typeResSvc = GetService (typeof (ITypeResolutionService));
			if (typeResSvc != null)
				return (typeResSvc as ITypeResolutionService).GetType (typeName);
			else
				return Type.GetType (typeName);
		}

		public IComponent RootComponent {
			get { return rootComponent; }
		}

		public Document RootDocument
		{
			get { return rootDocument; }
		}

		internal void SetRootComponent (IComponent rootComponent)
		{
			this.rootComponent = rootComponent;
			if (rootComponent == null) {
				rootDocument = null;
				return;
			}

			if (!(rootComponent is Control))
				throw new InvalidOperationException ("The root component must be a Control");
		}

		public string RootComponentClassName {
			get { return RootComponent.GetType ().Name; }
		}

		#endregion

		#region Transaction stuff

		private Stack transactionStack = new Stack ();

		public DesignerTransaction CreateTransaction (string description)
		{
			OnTransactionOpening ();
			Transaction trans = new Transaction (this, description);
			transactionStack.Push (trans);
			OnTransactionOpened ();

			return trans;
		}

		public DesignerTransaction CreateTransaction ()
		{
			return CreateTransaction (null);
		}

		public bool InTransaction
		{
			get { return (transactionStack.Count > 0); }
		}

		public string TransactionDescription
		{
			get {
				if (transactionStack.Count == 0)
					return null;
				else 
					return (transactionStack.Peek () as DesignerTransaction).Description;
			}
		}

		public event DesignerTransactionCloseEventHandler TransactionClosed;
		public event DesignerTransactionCloseEventHandler TransactionClosing;
		public event EventHandler TransactionOpened;
		public event EventHandler TransactionOpening;

		internal void OnTransactionClosed (bool commit, DesignerTransaction trans)
		{
			DesignerTransaction t = (DesignerTransaction) transactionStack.Pop();
			if (t != trans)
				throw new Exception ("Transactions cannot be closed out of order");
				
			if (TransactionClosed != null)
				TransactionClosed (this, new DesignerTransactionCloseEventArgs(commit));
		}

		internal void OnTransactionClosing (bool commit)
		{
			if (TransactionClosing != null)
				TransactionClosing (this, new DesignerTransactionCloseEventArgs(commit));
		}

		protected void OnTransactionOpening()
		{
			if (TransactionOpening != null)
				TransactionOpening (this, EventArgs.Empty);
		}

		protected void OnTransactionOpened ()
		{
			if (TransactionOpened != null)
				TransactionOpened (this, EventArgs.Empty);
		}
		
		#endregion

		#region Loading etc

		private bool loading = false;
		private bool activated = false;

		public event EventHandler Activated;
		public event EventHandler Deactivated;
		public event EventHandler LoadComplete;

		public void Activate ()
		{
			if (activated)
				throw new InvalidOperationException ("The host is already activated");

			//select the root component
			ISelectionService sel = GetService (typeof (ISelectionService)) as ISelectionService;
			if (sel == null)
				throw new Exception ("Could not obtain ISelectionService.");
			if (this.RootComponent == null)
				throw new InvalidOperationException ("The document must be loaded before the host can be activated");
			sel.SetSelectedComponents (new object[] {this.RootComponent});

			activated = true;
			OnActivated ();
		}

		public bool Loading {
			get { return loading; }
		}

		protected void OnLoadComplete ()
		{
			if (LoadComplete != null)
				LoadComplete (this, EventArgs.Empty);
		}

		protected void OnActivated ()
		{
			if (Activated != null)
				Activated (this, EventArgs.Empty);
		}

		protected void OnDeactivated ()
		{
			if (Deactivated != null)
				Deactivated (this, EventArgs.Empty);
		}
		
		public void NewFile ()
		{
			if (activated || RootComponent != null)
				throw new InvalidOperationException ("You must reset the host before loading another file.");
			loading = true;

			this.Container.Add (new WebFormPage ());
			this.rootDocument = new Document ((Control)rootComponent, this, "New Document");

			loading = false;
			OnLoadComplete ();
		}

//		public void Load (Stream file, string fileName)
//		{
//			using (TextReader reader = new StreamReader (file))
//			{
//				Load (reader.ReadToEnd (), fileName);
//			}
//		}
		
		public void LoadDocument ()
		{
			if (activated || RootComponent != null)
				throw new InvalidOperationException ("You must reset the host before loading another file.");
			loading = true;

			this.Container.Add (new WebFormPage());
			this.rootDocument = new Document ((Control)rootComponent, this);
			//rootDocument.Changed += new EventHandler (Document_OnChanged);

			serializer = new DocumentSerializer (this);
			designerSerializer = new DesignerSerializer (this);

			loading = false;
			OnLoadComplete ();
		}

		public void Reset ()
		{
			//container automatically destroys all children when this happens
			if (rootComponent != null)
				DestroyComponent (rootComponent);

			if (activated) {
				OnDeactivated ();
				this.activated = false;
			}
		}

		#endregion

		public void RootDesignerView_Realized (object o, EventArgs args)
		{
			System.Threading.Thread serializerThread = new System.Threading.Thread (new System.Threading.ThreadStart(InitialSerialization));
			serializerThread.Start ();
		}

		public void InitialSerialization ()
		{
			// check the document for controls and directives
			RootDocument.InitControlsAndDirectives ();

			// init the designer context tags, and find the absolute path to the current project
			var view = editorHost.DesignerView as AspNetEdit.Editor.UI.RootDesignerView;
			view.InitProperties ();

			// pass the freshly generated designer context to the html serializer
			serializer.SetDesignerContext (view.DesignerContext);

			// serialize the document for displaying in the designer
			SerializeDocument ();

			// subscribe to changes in the component container
			container.ComponentChanged += new ComponentChangedEventHandler (OnComponentUpdated);

			// serialize when a transaction is closed
			TransactionClosed += new DesignerTransactionCloseEventHandler (this_OnTransactionClosed);

			// subscibe for undo or redo events
			RootDocument.UndoRedo += document_OnUndoRedo;
		}

		public void this_OnTransactionClosed (object o, DesignerTransactionCloseEventArgs args)
		{
			if (args.TransactionCommitted && this.activated) {
				System.Threading.Thread serializerThread = new System.Threading.Thread (new System.Threading.ThreadStart(SerializeDocument));
				serializerThread.Start ();
			}
		}

		public void document_OnUndoRedo (object o, EventArgs evArgs)
		{
			System.Threading.Thread serializerThread = new System.Threading.Thread (new System.Threading.ThreadStart(SerializeDocumentHard));
			serializerThread.Start ();
		}

		public void SerializeDocument ()
		{
   			string html = serializer.GetDesignableHtml ();

			// fire the event
			OnDocumentChanged (html);
		}

		/// <summary>
		/// Serializes the document with re-checking the components for updated tags.
		/// </summary>
		public void SerializeDocumentHard ()
		{
			// unsubscribe for those events during the persisting of the document
			container.ComponentChanged -= OnComponentUpdated;
			TransactionClosed -= this_OnTransactionClosed;

			RootDocument.PersistControls ();
			string html = serializer.GetDesignableHtml ();
			OnDocumentChanged (html);

			container.ComponentChanged += OnComponentUpdated;
			TransactionClosed += this_OnTransactionClosed;
		}

		public class DocumentChangedEventArgs: EventArgs
		{
			string html;

			public DocumentChangedEventArgs (string newHtml) : base ()
			{
				html = newHtml;
			}

			public string Html {
				get { return html; }
			}
		}

		public delegate void DocumentChangedEventHandler (DocumentChangedEventArgs args);

		public event DocumentChangedEventHandler DocumentChanged;

		public void OnDocumentChanged (string newHtml)
		{
			if (DocumentChanged != null)
				DocumentChanged (new DocumentChangedEventArgs (newHtml));
		}

		public void OnComponentUpdated (object o, ComponentChangedEventArgs args)
		{
			if (activated) {
				// FIXME: a bug in ComponentChangedEventArgs - switches the return value of NewValue and OldValue
				// workaround for the bug
				// getting the new value directly from the component
				if (args.Member is PropertyDescriptor) {
					var propDesc = args.Member as PropertyDescriptor;
					object newVal = propDesc.GetValue (args.Component);
					UpdateControlTag (args.Component as IComponent, args.Member, newVal);
				}
			}
		}

		#region DesignerSerializer wrapper

		public void UpdateControlTag (IComponent comp, MemberDescriptor member, object newVal)
		{
			using (DesignerTransaction trans = CreateTransaction ("Updating component's tag: " + comp.Site.Name)) {
				designerSerializer.UpdateTag (comp, member, newVal);
				if (!InTransaction)
					trans.Commit ();
			}
		}

		public void UpdateControl (string id, string property, string newValue)
		{
			using (CreateTransaction ("Updating component: " + id)) {

			}
		}

		public void RemoveControl (IComponent comp)
		{
			using (DesignerTransaction trans = CreateTransaction ("Removing component: " + comp.Site.Name)) {
				designerSerializer.RemoveControlTag (comp.Site.Name);
				Container.Remove (comp);
				trans.Commit ();
			}
		}

		public void RemoveSelectedControls ()
		{
			using (DesignerTransaction trans = CreateTransaction ("Removing selected components")) {
				var selServ = GetService (typeof (ISelectionService)) as ISelectionService;
				if (selServ == null)
					throw new Exception ("Could not get selection service");
	
				ArrayList selectedItems = new ArrayList (selServ.GetSelectedComponents ());
	
				for (int i = selectedItems.Count - 1; i >= 0; i--) {
					var comp = selectedItems[i] as IComponent;

					if (RootComponent.Equals (comp))
						continue;

					designerSerializer.RemoveControlTag (comp.Site.Name);
					Container.Remove (comp);
				}
				trans.Commit ();
			}
		}

		#endregion

		#region Wrapping parent ServiceContainer

		public void AddService (Type serviceType, ServiceCreatorCallback callback, bool promote)
		{
			parentServices.AddService (serviceType, callback, promote);
		}

		public void AddService (Type serviceType, object serviceInstance, bool promote)
		{
			parentServices.AddService (serviceType, serviceInstance, promote);
		}

		public void AddService (Type serviceType, ServiceCreatorCallback callback)
		{
			parentServices.AddService (serviceType, callback);
		}

		public void AddService (Type serviceType, object serviceInstance)
		{
			parentServices.AddService (serviceType, serviceInstance);
		}

		public void RemoveService (Type serviceType, bool promote)
		{
			parentServices.RemoveService (serviceType, promote);
		}

		public void RemoveService (Type serviceType)
		{
			parentServices.RemoveService (serviceType);
		}

		public object GetService (Type serviceType)
		{
			object service = parentServices.GetService (serviceType);
			if (service != null)
				return service;
			else
				return null;
		}

		#endregion

		#region IDisposable Members

		private bool disposed = false;

		public void Dispose ()
		{
			if (!this.disposed) {
				//clean up the services we've registered
				parentServices.RemoveService (typeof (IComponentChangeService));
				parentServices.RemoveService (typeof (IDesignerHost));

				//and the container
				container.Dispose ();

				rootDocument.Destroy ();

				disposed = true;
			}
		}

		#endregion

		/*TODO: Some .NET 2.0 System.Web.UI.Design.WebFormsRootDesigner methods
		public abstract void RemoveControlFromDocument(Control control);
		public virtual void SetControlID(Control control, string id);
		public abstract string AddControlToDocument(Control newControl,	Control referenceControl, ControlLocation location);
		public virtual string GenerateEmptyDesignTimeHtml(Control control);
		public virtual string GenerateErrorDesignTimeHtml(Control control, Exception e, string errorMessage);
		*/
	}
}
