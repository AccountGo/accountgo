/// <binding BeforeBuild='Run - Development' />
var webpack = require('webpack');
var path = require('path');

var buildDir = path.resolve(__dirname, 'wwwroot/scripts');
var scriptsDir = path.resolve(__dirname, 'wwwroot/tsxbuild');

var host = "localhost";
if(process.env.APIHOST)
    host = process.env.APIHOST;

console.log("APIHOST environment variable in webpack is set to '" + host + "'. This can be change from Dockerfile e.g. 'RUN APIHOST=${APIHOST} webpack'. Or if manually running webpack, e.g. 'APIHOST=localhost webpack'");
console.log("API full url is " + JSON.stringify("http://" + host + ":8001/"));

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
        'Config':  JSON.stringify({apiUrl : "http://" + host + ":8001/"})
    }    
};

module.exports = config;