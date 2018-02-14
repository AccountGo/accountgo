/// <binding BeforeBuild='Run - Development' />
var webpack = require('webpack');
var path = require('path');

var buildDir = path.resolve(__dirname, './wwwroot/scripts');
var scriptsDir = path.resolve(__dirname, './wwwroot/libs/tsxbuild');

var config = {
    entry: {
        index: scriptsDir + '/home' + '/index',
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
    plugins: [
        new webpack.optimize.CommonsChunkPlugin({
            name: "vendor"
        }),
        new webpack.ProvidePlugin({
            $: 'jquery',
            jQuery: 'jquery',
            'window.jQuery': 'jquery',
            Popper: ['popper.js', 'default']
        })
    ],
    externals: {
        'Config': JSON.stringify(process.env.ENV === 'production' ?
            {
                apiUrl: "https://accountgoapi.azurewebsites.net/"
                //apiUrl: "http://localhost:5000/"
            } :
            {
                apiUrl: "https://accountgoapi.azurewebsites.net/"
                //apiUrl: "http://localhost:5000/"
            })
    }
};

module.exports = config;