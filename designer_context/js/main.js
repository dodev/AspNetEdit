jQuery (function () {
	var clicks = 0;
	jQuery ("body").mousedown (function () {
		jQuery ("title").html ("curent clicks in the body tag: " + (++clicks) );
	});
});

function SignalsManager () {
	this.Send = function (str) {
		jQuery ("title").html (str);
	};
	
	this.ChangeSelection = function (idArr) {
	};
}

function SelectionManager () {
	var SelectedIds = [];
	var PrimarySelection = null;
	
	var SelectItem = function (id, ctrl, shit) {
	};
}
