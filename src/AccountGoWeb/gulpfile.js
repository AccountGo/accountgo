/// <binding BeforeBuild='default' />
var gulp = require('gulp');

var destPath = './wwwroot/plugins/';

gulp.task("libs", () => {
    gulp.src([
            'bootstrap/dist/**',
            'bootstrap/less/**',
            'jquery/dist/**',
            'ag-grid/dist/**',
            'knockout/build/output/**',
            'knockout-mapping/dist/**'
    ],
    {
        cwd: "node_modules/**"
    })
        .pipe(gulp.dest("./wwwroot/plugins"));
});

gulp.task('default', ['libs']);