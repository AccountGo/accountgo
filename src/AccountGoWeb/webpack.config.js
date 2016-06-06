var webpack = require('webpack');
var path = require('path');

var buildDir = path.resolve(__dirname, './wwwroot/scripts');
var scriptsDir = path.resolve(__dirname, './wwwroot/libs/tsxout');

var config = {
    entry: {
        home: scriptsDir + '/home' + '/home.js',
        addSalesOrder: scriptsDir + '/sales/addsalesorder.js'
    },
    output: {
        path: buildDir,
        filename: '[name].chunk.js'
    },
    debug: true,
    devtool: 'source-map',
    module: {
        loaders: [
          {
              test: /\.tsx$/,
              loader: 'ts-loader',
              exclude: /(node_modules)/
          }
        ]
    }
};

module.exports = config;