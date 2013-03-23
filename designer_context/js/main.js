/*
	main.js - the actions that are performed on the load event of the document
	
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
jQuery (function () {
	// override the default action onclick for * elements
	// or following a link outside the designer
	jQuery ("*").click (function () {return false;});
	// disabling the selection of text with the mouse
	document.onmousedown = function() {return false;};
	// do not show the WebView's context menu
	document.oncontextmenu = function() {return false;};
	// initialize the signal manager
	signalMan.Initialize ();
	// initialize the selection manager
	selMan.Initialize ();
});
