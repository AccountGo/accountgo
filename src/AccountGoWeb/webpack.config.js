/// <binding BeforeBuild='Run - Development' />
var webpack = require('webpack');
var path = require('path');

var buildDir = path.resolve(__dirname, 'wwwroot/scripts');
var scriptsDir = path.resolve(__dirname, 'wwwroot/libs/tsxbuild');

var config = {
    entry: {
        index: scriptsDir + '/Home' + '/Index',
        "sales/salesorder": scriptsDir + '/Sales/SalesOrder',
        "quotations/salesquotation": scriptsDir + '/Quotations/SalesQuotation',
        "sales/salesinvoice": scriptsDir + '/Sales/SalesInvoice',
        "purchasing/purchaseorder": scriptsDir + '/Purchasing/PurchaseOrder',
        "purchasing/purchaseinvoice": scriptsDir + '/Purchasing/PurchaseInvoice',
        "financials/journalentry": scriptsDir + '/Financials/JournalEntry',
        vendor: ['react', 'react-dom']
    },
    output: {
        path: buildDir,
        filename: '[name].chunk.js'
    },
    optimization: {
        splitChunks: {
            cacheGroups: {
                commons: {
                    test: /[\\/]node_modules[\\/]/,
                    name: 'vendor',
                    chunks: 'all'
                }
            }
        }
    },
    resolve: {
        extensions: ['.js', '.jsx', '.tsx']
    },
    devtool: 'source-map',
    plugins: [
        new webpack.ProvidePlugin({
            $: 'jquery',
            jQuery: 'jquery',
            'window.jQuery': 'jquery',
            Popper: ['popper.js', 'default']
        })
    ],
    externals: {
        'Config': JSON.stringify(process.env.ENV === 'Production' ?
            {                
                apiUrl: "http://{0}:8001/"
            } :
            {
                apiUrl: "http://{0}:8001/"
            })
    }
};
debug.log(process.env.ASPNETCORE_URLS)
module.exports = config;