/*
	confing.js - declares variables related to naming conventions
			and style parameters

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

var markerConfig = {
	// width of the border, for calculating the correct position of the marker
	// declared in editor_style.css file
	borderWidth:2,
	// declared in editor_style.css file
	padding:	2
};

// names and prefixes for classes and ids in the designer surface
var noConflict = {
	prefix: 	"aspnetedit_",
	marker: 	"aspnetedit_control_marker",
	selected:	"aspnetedit_selected",
	forPrefix:	"aspnetedit_for_"
};

// name of the classes of the divs containing a json-serialized objects to
// used to initialize the front-end of the designer
var initParams = {
	selectable:	"aspnetedit_selectable_items",
	selected:	"aspnetedit_selected_items"
};
