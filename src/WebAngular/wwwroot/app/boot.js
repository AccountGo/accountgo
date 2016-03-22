System.register(['angular2/platform/browser', './app'], function(exports_1) {
    var browser_1, app_1;
    return {
        setters:[
            function (browser_1_1) {
                browser_1 = browser_1_1;
            },
            function (app_1_1) {
                app_1 = app_1_1;
            }],
        execute: function() {
            browser_1.bootstrap(app_1.App);
        }
    }
});
//# sourceMappingURL=boot.js.map