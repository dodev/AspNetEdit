/*
	SelectionManager.js - Tracks the seleted items in the designer surface
	
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
function SelectionManager () {
	var selectedIds = [];
	// index of the primary selection in the selectedIds array
	var primarySelection = -1;
	// used in methods, where the "this" global can be from other context
	var instance = this;
	
	// inits the markers' elements
	this.Initialize = function () {
		// get the the init params from the backend of the editor
		var selectable = JSON.parse (jQuery ("#" + initParams.selectable).html ());
		var selected = JSON.parse (jQuery ("#" + initParams.selected).html ());
		
		// add markers for all selectable items
		for (var id in selectable) {
			var el = jQuery ("#" + selectable[id]);
			if (el.length != 1)
				continue;

			var marker = jQuery (document.createElement ("div"));
			marker.addClass (noConflict.marker);
			// id of the marker contains a nonConflict prefix + the id of the item it
			// stands for
			marker.attr ("id", noConflict.forPrefix + selectable[id]);
			marker.bind ("mousedown", control_onMouseDown);
			// add the marker to the end of the body tag
			jQuery ("body").append (marker);
		}
		// place the markers above the controls
		this.PositionMarkers ();
		
		// show the selected items
		for (var id in selected) {
			this.Select (selected[id]);
		}
		
		// if the window is resized re-position the markers
		$(window).resize (this.PositionMarkers);
	};
	
	// refresh the position of the marker elements
	// they should have absolute for the css position property
	this.PositionMarkers = function () {
		jQuery ("." + noConflict.marker).each (function () {
			var id = instance.ExtractControlId (this.id);
			var el = jQuery ("#" + id);
			var offset = instance.GetCoordinates (el.get (0));
			offset.top -= markerConfig.padding + markerConfig.borderWidth;
			offset.left -= markerConfig.padding + markerConfig.borderWidth;
			//jQuery(this).offset (offset); //weird bug in .offset () prevent me from using it
			jQuery (this).css ("top", offset.top + "px");
			jQuery (this).css ("left", offset.left + "px");
			jQuery (this).width (el.outerWidth ());
			jQuery (this).height (el.outerHeight ());
		});
	};
	
	// combined method that adds an id to the list + shows the marker
	this.Select = function (id) {
		this.AddItem (id);
		this.ShowMarker (id);		
	};
	
	// removes an item from the list and hides marker
	this.Deselect = function (id) {
		this.RemoveItem (id);
		this.HideMarker (id);
	};
	
	this.ShowMarker = function (id) {
		jQuery ("#" + noConflict.forPrefix + id).addClass (noConflict.selected);
	};
	
	this.HideMarker = function (id) {
		jQuery ("#" + noConflict.forPrefix + id).removeClass (noConflict.selected);
	};
	
	this.ExtractControlId = function (markerId) {
		return markerId.split (noConflict.forPrefix) [1];
	};
	
	this.Count = function () {
		return selectedIds.length;
	};
	
	this.IsSelected = function (id) {
		if (selectedIds.indexOf (id) == -1)
			return false;
		else
			return true;
	};
	
	this.Flush = function () {
		selectedIds = [];
		primarySelection = -1;
		jQuery ("." + noConflict.selected).removeClass (noConflict.selected);
	};
	
	// adds the item from the selectedIds list
	this.AddItem = function (id) {
		if (selectedIds.indexOf (id) == -1) {
			selectedIds.push (id);
			
			primarySelection = selectedIds.length - 1; // the last pushed element
		}
	};
	
	this.SetPrimary = function (id) {
		var index = selectedIds.indexOf (id);
		if (index != -1)
			primarySelection = index;
	};
	
	// FIXME: not changing the primarySelection when removing items
	this.RemoveItem = function (id) {
		var index = -1;
		if ((index = selectedIds.indexOf(id)) != -1) {
			selectedIds.splice (index, 1);
			if (primarySelection == index) {
				if (selectedIds.length > 0)
					primarySelection = selectedIds.length - 1;
				else
					primarySelection = -1;
			}
		}
	};
	
	this.GetPrimary = function () {
		if (primarySelection == -1)
			return null;
		return selectedIds[primarySelection];
	};
	
	this.GetPrimaryIndex = function () {
		return primarySelection;
	};
	
	this.GetSelectedIds = function () {
		return selectedIds;
	};
	
	// for a javascript dom element, gets its absolute position
	// and returns an object with properties "top" and "lect"
	this.GetCoordinates = function (element) {
		var top = 0;
		var left = 0;
		
		while (element) {
			top += element.offsetTop;
			left += element.offsetLeft;
			// if the element doesn't have an offset parent
			// element is set to null, and the loop ends
			element = element.offsetParent;
		}
		
		return {"top": top, "left": left};
	};
}
