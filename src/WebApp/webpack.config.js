var webpack = require('webpack');
var path = require('path');

var buildDir = path.resolve(__dirname, './wwwroot/assets/dist/js');
var scriptsDir = path.resolve(__dirname, './wwwroot/libs/tsxout');

var config = {
    entry: {
        home: scriptsDir + '/home' + '/home.js',
        addSalesOrder: './Scripts/JSX/Sales/AddSalesOrder.jsx',
        salesOrders: './Scripts/JSX/Sales/SalesOrders.jsx'
    },
    output: {
        path: buildDir,
        filename: '[name].webpack.chunk.js'
    },
    module: {
        loaders: [
          {
              test: /\.jsx$/,
              loader: 'babel-loader',
              exclude: /(node_modules)/,
              query:
                  {
                      presets: ['es2015', 'react']
                  }
          }
        ]
    }
};

module.exports = config;