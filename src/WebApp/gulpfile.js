/// <binding BeforeBuild='default' />
var gulp = require('gulp');
var clean = require('gulp-clean');
var react = require('gulp-react');

var destPath = './wwwroot/libs/';

// Delete the libs directory
gulp.task('clean', function () {
    return gulp.src(destPath)
        .pipe(clean());
});

gulp.task("libs", () => {
    gulp.src([
            'bootstrap/**',
            'jquery/**',
            'react/**',
            'react-dom/**',
            'babel-core/**',
            'datatables/**',
            'datatables-bootstrap/**'
    ],
    {
        cwd: "node_modules/**"
    })
        .pipe(gulp.dest("./wwwroot/libs"));
});

gulp.task('transpile-js', function () {
    return gulp.src('./wwwroot/scripts/jsx/**/*.jsx')
      .pipe(react({ harmony: true }))
      .pipe(gulp.dest('./wwwroot/scripts/transpilejs'))
})

gulp.task('default', ['libs', 'transpile-js']);