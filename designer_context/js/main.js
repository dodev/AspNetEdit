var prefix = "aspnetedit_";
var classPrefix = "."+prefix;
var idPrefix = "#"+prefix;
var selMan = new SelectionManager ();
var signalMan = new SignalManager ();

function SignalManager () {
	this.Send = function (str) {
		jQuery ("title").html (str);
	};
	
	this.ChangeSelection = function (idArr, primary) {
		var args = JSON.stringify ({
			"SelectedIds" : idArr,
			"PrimarySelection": primary
		});
		var msg = new Message ("selection_changed", args);
		
		this.Send (JSON.stringify (msg));
	};
}

// Tracks the seleted items in the designer surface
function SelectionManager () {
	var selectedIds = [];
	var primarySelection = -1;
	
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
	
	this.CommitChanges = function () {
		signalMan.ChangeSelection (selectedIds, primarySelection);	
	};
}

function ExtractIdFromContainer (tag) {
	return jQuery (tag).children ().first ().attr ("id");
}

function Message (name, args) {
	this.MsgName = name;
	this.Arguments = args;
}
