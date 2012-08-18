/*
* UndoTracker.cs
* 
* Authors: 
*  Petar Dodev <petar.dodev@gmail.com>
*
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
namespace AspNetEdit.Tools
{
	public class UndoTracker
	{
		int undoQueue;
		int redoQueue;

		public UndoTracker ()
		{
			undoQueue = 0;
			redoQueue = 0;
		}

		public UndoTracker (int undo, int redo)
		{
			undoQueue = undo;
			redoQueue = redo;
		}

		public void FinishAction ()
		{
			redoQueue = 0;
			undoQueue++;
		}

		public void UndoAction ()
		{
			if (undoQueue > 0) {
				undoQueue--;
				redoQueue++;
			}
		}
		
		public void RedoAction ()
		{
			if (redoQueue > 0) {
				redoQueue--;
				undoQueue++;
			}
		}

		public bool CanUndo
		{
			get {
				return undoQueue > 0;
			}
		}

		public bool CanRedo
		{
			get {
				return redoQueue > 0;
			}
		}
	}
}

