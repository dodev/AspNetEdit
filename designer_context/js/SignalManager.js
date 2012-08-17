/*
	SignalManager.js - sends JSON messages to the Editor's backend
	
      Copyright 2012 Petar Dodev

   Licensed under the Apache License, Version 2.0 (the "License");
   you may not use this file except in compliance with the License.
   You may obtain a copy of the License at

       http://www.apache.org/licenses/LICENSE-2.0

   Unless required by applicable law or agreed to in writing, software
   distributed under the License is distributed on an "AS IS" BASIS,
   WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied.
   See the License for the specific language governing permissions and
   limitations under the License.
*/
function SignalManager () {
	// an event handler waits in the C# back-end of the editor for 
	// changes in the title tag of the document, so to send a message
	// the innerHTML of the title is changed.
	// The messages that the back-end expect are JSON serialized objects
	// consisted of an MsgName property with the name of the type of message
	// that was sent. The second part of the property is called Arguments.
	// It is a string field which contains another JSON serialized object
	// depending on the type of message send. Using nested JSON strings
	// allows the messages to pass different arguments for different MsgName.
	// The other objects of the designer call methods of the SignalManager
	// which methods generate the Message object with the corresponding
	// Arguments string and passes it to the Send method, which serializes
	// it and sets the title tags innerHTML to the resulting JSON string.
	
	// make sure there is a head tag in the document
	this.Initialize = function () {
		if (jQuery ("title").length < 1) {
			jQuery ("head").append (document.createElement ("title"));			
		}
	};
	
	// sets the title tag to the message string
	this.Send = function (msg) {
		jQuery ("title").html (JSON.stringify (msg));
	};
	
	// send a selection_changed message
	// the primary parameter is an int - index of the primary selection
	// in the idArr
	this.ChangeSelection = function (idArr, primary) {
		var args = {
			"SelectedIds" : idArr,
			"PrimarySelection": primary
		};
		var msg = new Message ("selection_changed", args);
		
		this.Send (msg);
	};
	
	// send a context_menu_request msg with the coordinates of the click
	// and id of the control
	this.ShowContextMenu = function (x, y, id) {
		var args = {
			"ComponentId" : id,
			"X" : x,
			"Y" : y
		};
		
		var msg = new Message ("context_menu_request", args);
		this.Send (msg);
	};
}

function Message (name, args) {
	// type of the message
	this.MsgName = name;
	// JSON string from the arguments object for the corresponding MsgName
	this.Arguments = JSON.stringify (args);
}
