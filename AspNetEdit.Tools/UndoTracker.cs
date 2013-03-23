/*
* UndoTracker.cs - a simple undo tracker that counts the actions
* 				that were performed and those that can be undone
* 				or redone.
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
		/// <summary>
		/// Length of the undo queue
		/// </summary>
		int undoQueue;
		/// <summary>
		/// Length of the redo queue.
		/// </summary>
		int redoQueue;

		/// <summary>
		/// Initializes a new instance of the <see cref="AspNetEdit.Tools.UndoTracker"/> class
		/// with zero values for the length of the queues
		/// </summary>
		public UndoTracker ()
		{
			undoQueue = 0;
			redoQueue = 0;
		}

		/// <summary>
		/// Initializes a new instance of the <see cref="AspNetEdit.Tools.UndoTracker"/> class
		/// </summary>
		/// <param name='undo'>
		/// Current length of the undo queue
		/// </param>
		/// <param name='redo'>
		/// Current length of the redo queue
		/// </param>
		public UndoTracker (int undo, int redo)
		{
			undoQueue = undo;
			redoQueue = redo;
		}

		/// <summary>
		/// Add one action to the undo queue and flush the redo queue.
		/// </summary>
		public void FinishAction ()
		{
			redoQueue = 0;
			undoQueue++;
		}

		/// <summary>
		/// Undo an action.
		/// </summary>
		public void UndoAction ()
		{
			if (undoQueue > 0) {
				undoQueue--;
				redoQueue++;
			}
		}

		/// <summary>
		/// Redos an action.
		/// </summary>
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

