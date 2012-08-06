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
	this.Send = function (msg) {
		jQuery ("title").html (JSON.stringify (msg));
	};
	
	this.ChangeSelection = function (idArr, primary) {
		var args = {
			"SelectedIds" : idArr,
			"PrimarySelection": primary
		};
		var msg = new Message ("selection_changed", args);
		
		this.Send (msg);
	};
	
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
	this.MsgName = name;
	this.Arguments = JSON.stringify (args);
}
