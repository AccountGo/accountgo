/// <binding BeforeBuild='Run - Development' />
var webpack = require('webpack');
var path = require('path');

var buildDir = path.resolve(__dirname, 'wwwroot/scripts');
var scriptsDir = path.resolve(__dirname, 'Scripts');
console.log("NODE_ENV (webpack.config.js) => " + process.env.npm_config_nodeenv);
console.log("APIURLSPA (webpack.config.js) => " + process.env.npm_config_apiurlspa);

var apiUrl = process.env.npm_config_apiurlspa;
if (apiUrl === 'undefined' || apiUrl == null || apiUrl === '') {
    apiUrl = "http://localhost:8000/spaproxy?endpoint=";
    console.log("[webpack.config.js] APIURL environment variable not found. apiUrl is set to default => " + apiUrl);
}

var mode = process.env.npm_config_nodeenv;
if (mode === 'undefined' || mode == null || mode === '') {
    mode = "development";
}

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
    module: {
        rules: [
            {
                test: /\.tsx?$/,
                loader: require.resolve('ts-loader'),
                exclude: /node_modules/
            }
        ]
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
        'Config': JSON.stringify({ apiUrl: apiUrl, env: mode })
    }
};

module.exports = config;