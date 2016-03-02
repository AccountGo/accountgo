/// <binding AfterBuild='lib, rxjs' Clean='clean' />

var gulp = require('gulp');
var rimraf = require('rimraf');

var paths = {
    npm: './node_modules/',
    lib: './wwwroot/lib/'
};

var libs = [
    paths.npm + 'angular2/bundles/angular2.dev.js',
    paths.npm + 'angular2/bundles/http.dev.js',
    paths.npm + 'angular2/bundles/angular2-polyfills.js',
    paths.npm + 'es6-shim/es6-shim.js',
    paths.npm + 'systemjs/dist/system.js',
    paths.npm + 'systemjs/dist/system-polyfills.js'
];

gulp.task('rxjs', function () {
    return gulp.src(paths.npm + 'rxjs/**/*.js').pipe(gulp.dest(paths.lib + 'rxjs/'));
});

gulp.task('lib', ['rxjs'], function () {
    return gulp.src(libs).pipe(gulp.dest(paths.lib));
});

gulp.task('clean', function (callback) {
    rimraf(paths.lib, callback);
});