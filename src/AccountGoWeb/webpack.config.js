var webpack = require('webpack');
var path = require('path');

var buildDir = path.resolve(__dirname, './wwwroot/scripts');
var scriptsDir = path.resolve(__dirname, './wwwroot/libs/tsxbuild');
//var scriptsDir = path.resolve(__dirname, './Scripts');

var config = {
    entry: {
        home: scriptsDir + '/home' + '/home',
        "sales/salesorders": scriptsDir + '/sales/salesorders',
        vendor: ['react', 'react-dom']
    },
    output: {
        path: buildDir,
        filename: '[name].chunk.js'
    },
    resolve: {
        extensions: ['', '.js', '.jsx', '.tsx']
    },
    debug: true,
    devtool: 'source-map',
    //module: {
    //    loaders: [
    //      {
    //          test: /\.tsx$/,
    //          loader: 'ts-loader',
    //          exclude: /(node_modules)/
    //      }
    //    ]
    //},
    plugins: [
        new webpack.optimize.CommonsChunkPlugin("vendor", "vendor.bundle.js")
    ],
};

module.exports = config;