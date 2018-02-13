/// <binding BeforeBuild='Run - Development' />
var webpack = require('webpack');
var path = require('path');

var buildDir = path.resolve(__dirname, './wwwroot/scripts');
var scriptsDir = path.resolve(__dirname, './wwwroot/libs/tsxbuild');
//var scriptsDir = path.resolve(__dirname, './Scripts');

var config = {
    entry: {
        home: scriptsDir + '/home' + '/home',
        "sales/salesorder": scriptsDir + '/sales/salesorder',
        "quotations/salesquotation": scriptsDir + '/quotations/salesquotation',
        "sales/salesinvoice": scriptsDir + '/sales/salesinvoice',
        "purchasing/purchaseorder": scriptsDir + '/purchasing/purchaseorder',
        "purchasing/purchaseinvoice": scriptsDir + '/purchasing/purchaseinvoice',
        "financials/journalentry": scriptsDir + '/financials/journalentry',
        vendor: ['react', 'react-dom']
    },
    output: {
        path: buildDir,
        filename: '[name].chunk.js'
    },
    resolve: {
        extensions: ['.js', '.jsx', '.tsx']
    },
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
        new webpack.optimize.CommonsChunkPlugin({
            name: "vendor"
        })
    ],
    externals: {
        'Config': JSON.stringify(process.env.ENV === 'production' ?
            {
                apiUrl: "https://accountgoapi.azurewebsites.net/"
            } :
            {
                apiUrl: "https://accountgoapi.azurewebsites.net/"
            })
    }
};

module.exports = config;