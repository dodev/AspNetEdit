/*
* MonoDevelopProxy.cs: Proxies methods that run in the MD process so 
*    that they are remotely accessible to the AspNetEdit process.
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
using System.CodeDom;
using System.Collections.Generic;
using System.Reflection;

using MonoDevelop.Ide;
using MonoDevelop.Ide.TypeSystem;
using MonoDevelop.Core;
using MonoDevelop.Projects;
using MonoDevelop.DesignerSupport;
using ICSharpCode.NRefactory.TypeSystem;
using ICSharpCode.NRefactory.TypeSystem.Implementation;

namespace AspNetEdit.Integration
{
	
	public class MonoDevelopProxy : MarshalByRefObject, IDisposable
	{
		Project project;
		IType fullClass;
		IUnresolvedTypeDefinition nonDesignerClass;
		
		public MonoDevelopProxy (Project project, string className)
		{
			this.project = project;
			
			if (className != null) {
				ICompilation compilation = TypeSystemService.GetProjectContext (project).CreateCompilation ();
				foreach (ITypeDefinition itd in compilation.MainAssembly.TopLevelTypeDefinitions) {
					if (itd.FullName == className)
						fullClass = itd as IType;
				}
			}				
			else
				fullClass = null;
			
			if (fullClass != null)
				nonDesignerClass = MonoDevelop.DesignerSupport.CodeBehind.GetNonDesignerClass (fullClass);
			else
				nonDesignerClass = null;
			
		}
		
		//keep this object available through remoting
		public override object InitializeLifetimeService ()
		{
			return null;
		}
		
		bool disposed = false;
		public void Dispose ()
		{
			if (disposed)
				return;
			disposed = true;
			
			//The proxy uses InitializeLifetimeService to make sure it stays connected
			//So need to make sure we don't keep it around forever
			System.Runtime.Remoting.RemotingServices.Disconnect (this);
		}
		
		//TODO: make this work with inline code
		#region event binding

		public bool IdentifierExistsInCodeBehind (string trialIdentifier)
		{
			if (fullClass == null)
				return false;
			
			return BindingService.IdentifierExistsInClass (fullClass, trialIdentifier);
		}
		
		public string GenerateIdentifierUniqueInCodeBehind (string trialIdentifier)
		{
			if (fullClass == null)
				return trialIdentifier;
			
			return BindingService.GenerateIdentifierUniqueInClass (fullClass, trialIdentifier);
		}
		
		
		public string[] GetCompatibleMethodsInCodeBehind (MethodInfo methodInfo)
		{
			if (fullClass == null)
				return new string[0];

			ParameterInfo[] reflectionParams = methodInfo.GetParameters ();
			List<IMethod> compatMeth = new List<IMethod> ();
			IType[] pars = new IType[reflectionParams.Length];
			List<IType>[] baseTypes = new List<IType>[reflectionParams.Length];
			ICompilation compilation = TypeSystemService.GetCompilation (IdeApp.Workbench.ActiveDocument.Project);

			for (int i = 0; i < reflectionParams.Length; i++) {
				pars[i] = reflectionParams[i].ParameterType.ToTypeReference ().Resolve (compilation);
				baseTypes[i] = new List<IType> (pars[i].GetAllBaseTypes ());
			}

			var matchMethType = methodInfo.ReturnType.ToTypeReference ().Resolve (compilation);

			foreach (IMethod mmethod in fullClass.GetMethods (null, null,GetMemberOptions.IgnoreInheritedMembers)) {
				if (mmethod.IsPrivate || mmethod.Parameters.Count != pars.Length || mmethod.IsInternal)
					continue;

				if (mmethod.ReturnType.FullName != matchMethType.FullName)
					continue;

				bool allCompatible = true;
				
				//compare each parameter
				for (int i = 0; i < pars.Length; i++) {
					if (pars[i].FullName != mmethod.Parameters[i].Type.FullName) {
						allCompatible = false;
						break;
					}

					List<IType> insideBTypes = new List<IType> (mmethod.Parameters[i].Type.GetAllBaseTypes ());
					if (insideBTypes.Count != baseTypes[i].Count) {
						allCompatible = false;
						break;
					}
					for (int j = 0; j < baseTypes[i].Count; j++) {
						if (baseTypes[i][j].FullName != insideBTypes[j].FullName) {
							allCompatible = false;
							break;
						}
					}
				}
				
				if (allCompatible)
					compatMeth.Add (mmethod);
			}

			string[] names = new string[compatMeth.Count];
			for (int i = 0; i < names.Length; i++)
				names[i] = compatMeth[i].Name;
			return names;
		}
		
		public bool ShowMethod (CodeMemberMethod method)
		{
			if (nonDesignerClass == null)
				return false;
			
			Gtk.Application.Invoke (delegate {
				BindingService.CreateAndShowMember (project, fullClass.GetDefinition (), nonDesignerClass, method); 
			});
			
			return true;
		}
		
		public bool ShowLine (int lineNumber)
		{
			if (nonDesignerClass == null)
				return false;
			
			Gtk.Application.Invoke (delegate {
				IdeApp.Workbench.OpenDocument (
					new FilePath (nonDesignerClass.ParsedFile.FileName),
					lineNumber, 
					1,
					MonoDevelop.Ide.Gui.OpenDocumentOptions.Default);
			});
			
			return true;
		}

		#endregion event binding
	}
}
