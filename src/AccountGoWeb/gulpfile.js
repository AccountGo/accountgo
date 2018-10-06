/// <binding BeforeBuild='default' />
var gulp = require('gulp');

var destPath = './wwwroot/lib/';

gulp.task("libs", () => {
    gulp.src([
        '@coreui/coreui/dist/**',
        '@coreui/icons/css/**',
        'simple-line-icons/css/**',
        'simple-line-icons/fonts/**',
        'ionicons/dist/**',
        'bootstrap/dist/**',
        'perfect-scrollbar/dist/**',
        'jquery/dist/**',
        'ag-grid/dist/**',
        'font-awesome/css/**',
        'font-awesome/fonts/**',
        'knockout/build/output/**',
        'knockout-mapping/dist/**',
        'd3/dist/**',
        'jspdf/dist/**',
        'accounting/**',
        'html2canvas/dist/**',
        'popper.js/dist/**',
        'pace-progress/**'
    ],
        {
            cwd: "node_modules/**"
        })
        .pipe(gulp.dest(destPath));
});

gulp.task('default', ['libs']);