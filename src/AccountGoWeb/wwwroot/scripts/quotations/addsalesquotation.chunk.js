webpackJsonp([4],{

/***/ 0:
/*!***************************************************************!*\
  !*** ./wwwroot/libs/tsxbuild/quotations/addsalesquotation.js ***!
  \***************************************************************/
/***/ function(module, exports, __webpack_require__) {

	"use strict";
	var __extends = (this && this.__extends) || function (d, b) {
	    for (var p in b) if (b.hasOwnProperty(p)) d[p] = b[p];
	    function __() { this.constructor = d; }
	    d.prototype = b === null ? Object.create(b) : (__.prototype = b.prototype, new __());
	};
	var __decorate = (this && this.__decorate) || function (decorators, target, key, desc) {
	    var c = arguments.length, r = c < 3 ? target : desc === null ? desc = Object.getOwnPropertyDescriptor(target, key) : desc, d;
	    if (typeof Reflect === "object" && typeof Reflect.decorate === "function") r = Reflect.decorate(decorators, target, key, desc);
	    else for (var i = decorators.length - 1; i >= 0; i--) if (d = decorators[i]) r = (c < 3 ? d(r) : c > 3 ? d(target, key, r) : d(target, key)) || r;
	    return c > 3 && r && Object.defineProperty(target, key, r), r;
	};
	var React = __webpack_require__(/*! react */ 1);
	var ReactDOM = __webpack_require__(/*! react-dom */ 38);
	var mobx_react_1 = __webpack_require__(/*! mobx-react */ 168);
	var SelectCustomer_1 = __webpack_require__(/*! ../Shared/Components/SelectCustomer */ 207);
	var SelectPaymentTerm_1 = __webpack_require__(/*! ../Shared/Components/SelectPaymentTerm */ 198);
	var SelectLineItem_1 = __webpack_require__(/*! ../Shared/Components/SelectLineItem */ 199);
	var SelectLineMeasurement_1 = __webpack_require__(/*! ../Shared/Components/SelectLineMeasurement */ 200);
	var SalesQuotationStore_1 = __webpack_require__(/*! ../Shared/Stores/Quotations/SalesQuotationStore */ 208);
	var store = new SalesQuotationStore_1.default();
	var SaveQuotationButton = (function (_super) {
	    __extends(SaveQuotationButton, _super);
	    function SaveQuotationButton() {
	        _super.apply(this, arguments);
	    }
	    SaveQuotationButton.prototype.saveNewSalesQuotation = function (e) {
	        store.saveNewQuotation();
	    };
	    SaveQuotationButton.prototype.render = function () {
	        return (React.createElement("input", {type: "button", value: "Save", onClick: this.saveNewSalesQuotation.bind(this)}));
	    };
	    return SaveQuotationButton;
	}(React.Component));
	var CancelQuotationButton = (function (_super) {
	    __extends(CancelQuotationButton, _super);
	    function CancelQuotationButton() {
	        _super.apply(this, arguments);
	    }
	    CancelQuotationButton.prototype.render = function () {
	        return (React.createElement("input", {type: "button", value: "Cancel"}));
	    };
	    return CancelQuotationButton;
	}(React.Component));
	var SalesQuotationHeader = (function (_super) {
	    __extends(SalesQuotationHeader, _super);
	    function SalesQuotationHeader() {
	        _super.apply(this, arguments);
	    }
	    SalesQuotationHeader.prototype.onChangeQuotationDate = function (e) {
	        store.changedQuotationDate(e.target.value);
	    };
	    SalesQuotationHeader.prototype.render = function () {
	        return (React.createElement("div", {className: "box"}, React.createElement("div", {className: "box-header with-border"}, React.createElement("h3", {className: "box-title"}, "Customer Information"), React.createElement("div", {className: "box-tools pull-right"}, React.createElement("button", {type: "button", className: "btn btn-box-tool", "data-widget": "collapse", "data-toggle": "tooltip", title: "Collapse"}, React.createElement("i", {className: "fa fa-minus"})))), React.createElement("div", {className: "box-body"}, React.createElement("div", {className: "col-sm-6"}, React.createElement("div", {className: "row"}, React.createElement("div", {className: "col-sm-2"}, "Customer"), React.createElement("div", {className: "col-sm-10"}, React.createElement(SelectCustomer_1.default, {store: store}))), React.createElement("div", {className: "row"}, React.createElement("div", {className: "col-sm-2"}, "Payment Term"), React.createElement("div", {className: "col-sm-10"}, React.createElement(SelectPaymentTerm_1.default, {store: store})))), React.createElement("div", {className: "col-md-6"}, React.createElement("div", {className: "row"}, React.createElement("div", {className: "col-sm-2"}, "Date"), React.createElement("div", {className: "col-sm-10"}, React.createElement("input", {type: "date", className: "form-control pull-right", onChange: this.onChangeQuotationDate.bind(this), defaultValue: store.salesQuotation.quotationDate}))), React.createElement("div", {className: "row"}, React.createElement("div", {className: "col-sm-2"}, "Reference no."), React.createElement("div", {className: "col-sm-10"}, React.createElement("input", {type: "text", className: "form-control"})))))));
	    };
	    SalesQuotationHeader = __decorate([
	        mobx_react_1.observer
	    ], SalesQuotationHeader);
	    return SalesQuotationHeader;
	}(React.Component));
	var SalesQuotationLines = (function (_super) {
	    __extends(SalesQuotationLines, _super);
	    function SalesQuotationLines() {
	        _super.apply(this, arguments);
	    }
	    SalesQuotationLines.prototype.addLineItem = function () {
	        var itemId, measurementId, quantity, amount, discount;
	        itemId = document.getElementById("optNewItemId").value;
	        measurementId = document.getElementById("optNewMeasurementId").value;
	        quantity = document.getElementById("txtNewQuantity").value;
	        amount = document.getElementById("txtNewAmount").value;
	        discount = document.getElementById("txtNewDiscount").value;
	        store.addLineItem(itemId, measurementId, quantity, amount, discount);
	        document.getElementById("txtNewQuantity").value = "1";
	        document.getElementById("txtNewAmount").value = "0";
	        document.getElementById("txtNewDiscount").value = "0";
	    };
	    SalesQuotationLines.prototype.onClickRemoveLineItem = function (e) {
	        store.removeLineItem(e.target.name);
	    };
	    SalesQuotationLines.prototype.onChangeQuantity = function (e) {
	        store.updateLineItem(e.target.name, "quantity", e.target.value);
	    };
	    SalesQuotationLines.prototype.onChangeAmount = function (e) {
	        store.updateLineItem(e.target.name, "amount", e.target.value);
	    };
	    SalesQuotationLines.prototype.onChangeDiscount = function (e) {
	        store.updateLineItem(e.target.name, "discount", e.target.value);
	    };
	    SalesQuotationLines.prototype.render = function () {
	        var lineItems = [];
	        for (var i = 0; i < store.salesQuotation.salesQuotationLines.length; i++) {
	            lineItems.push(React.createElement("tr", {key: i}, React.createElement("td", null, React.createElement(SelectLineItem_1.default, {store: store, row: i, selected: store.salesQuotation.salesQuotationLines[i].itemId})), React.createElement("td", null, store.salesQuotation.salesQuotationLines[i].itemId), React.createElement("td", null, React.createElement(SelectLineMeasurement_1.default, {row: i, store: store, selected: store.salesQuotation.salesQuotationLines[i].measurementId})), React.createElement("td", null, React.createElement("input", {className: "form-control", type: "text", name: i, value: store.salesQuotation.salesQuotationLines[i].quantity, onChange: this.onChangeQuantity.bind(this)})), React.createElement("td", null, React.createElement("input", {className: "form-control", type: "text", name: i, value: store.salesQuotation.salesQuotationLines[i].amount, onChange: this.onChangeAmount.bind(this)})), React.createElement("td", null, React.createElement("input", {className: "form-control", type: "text", name: i, value: store.salesQuotation.salesQuotationLines[i].discount, onChange: this.onChangeDiscount.bind(this)})), React.createElement("td", null, store.lineTotal(i)), React.createElement("td", null, React.createElement("input", {type: "button", name: i, value: "Remove", onClick: this.onClickRemoveLineItem.bind(this)}))));
	        }
	        return (React.createElement("div", {className: "box"}, React.createElement("div", {className: "box-header with-border"}, React.createElement("h3", {className: "box-title"}, "Line Items"), React.createElement("div", {className: "box-tools pull-right"}, React.createElement("button", {type: "button", className: "btn btn-box-tool", "data-widget": "collapse", "data-toggle": "tooltip", title: "Collapse"}, React.createElement("i", {className: "fa fa-minus"})))), React.createElement("div", {className: "box-body table-responsive"}, React.createElement("table", {className: "table table-hover"}, React.createElement("thead", null, React.createElement("tr", null, React.createElement("td", null, "Item Id"), React.createElement("td", null, "Item Name"), React.createElement("td", null, "Measurement"), React.createElement("td", null, "Quantity"), React.createElement("td", null, "Amount"), React.createElement("td", null, "Discount"), React.createElement("td", null, "Line Total"), React.createElement("td", null))), React.createElement("tbody", null, lineItems, React.createElement("tr", null, React.createElement("td", null, React.createElement(SelectLineItem_1.default, {store: store, controlId: "optNewItemId"})), React.createElement("td", null, "Item Name"), React.createElement("td", null, React.createElement(SelectLineMeasurement_1.default, {store: store, controlId: "optNewMeasurementId"})), React.createElement("td", null, React.createElement("input", {className: "form-control", type: "text", id: "txtNewQuantity"})), React.createElement("td", null, React.createElement("input", {className: "form-control", type: "text", id: "txtNewAmount"})), React.createElement("td", null, React.createElement("input", {className: "form-control", type: "text", id: "txtNewDiscount"})), React.createElement("td", null), React.createElement("td", null, React.createElement("input", {type: "button", value: "Add", onClick: this.addLineItem}))))))));
	    };
	    SalesQuotationLines = __decorate([
	        mobx_react_1.observer
	    ], SalesQuotationLines);
	    return SalesQuotationLines;
	}(React.Component));
	var SalesQuotationTotals = (function (_super) {
	    __extends(SalesQuotationTotals, _super);
	    function SalesQuotationTotals() {
	        _super.apply(this, arguments);
	    }
	    SalesQuotationTotals.prototype.render = function () {
	        return (React.createElement("div", {className: "box"}, React.createElement("div", {className: "box-body"}, React.createElement("div", {className: "row"}, React.createElement("div", {className: "col-md-2"}, React.createElement("label", null, "Running Total: ")), React.createElement("div", {className: "col-md-2"}, 0), React.createElement("div", {className: "col-md-2"}, React.createElement("label", null, "Tax Total: ")), React.createElement("div", {className: "col-md-2"}, 0), React.createElement("div", {className: "col-md-2"}, React.createElement("label", null, "Grand Total: ")), React.createElement("div", {className: "col-md-2"}, store.grandTotal())))));
	    };
	    SalesQuotationTotals = __decorate([
	        mobx_react_1.observer
	    ], SalesQuotationTotals);
	    return SalesQuotationTotals;
	}(React.Component));
	var AddSalesQuotation = (function (_super) {
	    __extends(AddSalesQuotation, _super);
	    function AddSalesQuotation() {
	        _super.apply(this, arguments);
	    }
	    AddSalesQuotation.prototype.render = function () {
	        return (React.createElement("div", null, React.createElement(SalesQuotationHeader, null), React.createElement(SalesQuotationLines, null), React.createElement(SalesQuotationTotals, null), React.createElement("div", null, React.createElement(SaveQuotationButton, null), React.createElement(CancelQuotationButton, null))));
	    };
	    return AddSalesQuotation;
	}(React.Component));
	Object.defineProperty(exports, "__esModule", { value: true });
	exports.default = AddSalesQuotation;
	ReactDOM.render(React.createElement(AddSalesQuotation, null), document.getElementById("divAddSalesQuotation"));
	//# sourceMappingURL=AddSalesQuotation.js.map

/***/ },

/***/ 168:
/*!*******************************!*\
  !*** ./~/mobx-react/index.js ***!
  \*******************************/
/***/ function(module, exports, __webpack_require__) {

	(function() {
	    function mrFactory(mobx, React, ReactDOM) {
	        if (!mobx)
	            throw new Error("mobx-react requires the MobX package")
	        if (!React)
	            throw new Error("mobx-react requires React to be available");
	
	        var isDevtoolsEnabled = false;
	
	        // WeakMap<Node, Object>;
	        var componentByNodeRegistery = typeof WeakMap !== "undefined" ? new WeakMap() : undefined;
	        var renderReporter = new EventEmitter();
	        function findDOMNode(component) {
	            if (ReactDOM)
	                return ReactDOM.findDOMNode(component);
	            return null;
	        }
	
	        function reportRendering(component) {
	            var node = findDOMNode(component);
	            if (node)
	                componentByNodeRegistery.set(node, component);
	
	            renderReporter.emit({
	                event: 'render',
	                renderTime: component.__$mobRenderEnd - component.__$mobRenderStart,
	                totalTime: Date.now() - component.__$mobRenderStart,
	                component: component,
	                node: node
	            });
	        }
	
	        var reactiveMixin = {
	            componentWillMount: function() {
	                // Generate friendly name for debugging
	                var name = [
	                    this.displayName || this.name || (this.constructor && (this.constructor.displayName || this.constructor.name)) || "<component>",
	                    "#", this._reactInternalInstance && this._reactInternalInstance._rootNodeID,
	                    ".render()"
	                ].join("");
	
	                var baseRender = this.render.bind(this);
	                var self = this;
	                var reaction = null;
	                var isRenderingPending = false;
	                function initialRender() {
	                    reaction = new mobx.Reaction(name, function() {
	                        if (!isRenderingPending) {
	                            isRenderingPending = true;
	                            if (typeof self.componentWillReact === "function")
	                                self.componentWillReact();
	                            React.Component.prototype.forceUpdate.call(self)
	                        }
	                    });
	                    reactiveRender.$mobx = reaction;
	                    self.render = reactiveRender;
	                    return reactiveRender();
	                }
	
	                function reactiveRender() {
	                    isRenderingPending = false;
	                    var rendering;
	                    reaction.track(function() {
	                        if (isDevtoolsEnabled)
	                            self.__$mobRenderStart = Date.now();
	                        rendering = mobx.extras.allowStateChanges(false, baseRender);
	                        if (isDevtoolsEnabled)
	                            self.__$mobRenderEnd = Date.now();
	                    });
	                    return rendering;
	                }
	
	                this.render = initialRender;
	            },
	
	            componentWillUnmount: function() {
	                this.render.$mobx && this.render.$mobx.dispose();
	                if (isDevtoolsEnabled) {
	                    var node = findDOMNode(this);
	                    if (node) {
	                        componentByNodeRegistery.delete(node);
	                    }
	                    renderReporter.emit({
	                        event: 'destroy',
	                        component: this,
	                        node: node
	                    });
	                }
	            },
	
	            componentDidMount: function() {
	                if (isDevtoolsEnabled)
	                    reportRendering(this);
	            },
	
	            componentDidUpdate: function() {
	                if (isDevtoolsEnabled)
	                    reportRendering(this);
	            },
	
	            shouldComponentUpdate: function(nextProps, nextState) {
	                // TODO: if context changed, return true.., see #18
	                
	                // if props or state did change, but a render was scheduled already, no additional render needs to be scheduled
	                if (this.render.$mobx && this.render.$mobx.isScheduled() === true)
	                    return false;
	                
	                // update on any state changes (as is the default)
	                if (this.state !== nextState)
	                    return true;
	                // update if props are shallowly not equal, inspired by PureRenderMixin
	                var keys = Object.keys(this.props);
	                var key;
	                if (keys.length !== Object.keys(nextProps).length)
	                    return true;
	                for(var i = keys.length -1; i >= 0, key = keys[i]; i--) {
	                    var newValue = nextProps[key];
	                    if (newValue !== this.props[key]) {
	                        return true;
	                    } else if (newValue && typeof newValue === "object" && !mobx.isObservable(newValue)) {
	                        /**
	                         * If the newValue is still the same object, but that object is not observable,
	                         * fallback to the default React behavior: update, because the object *might* have changed.
	                         * If you need the non default behavior, just use the React pure render mixin, as that one
	                         * will work fine with mobx as well, instead of the default implementation of
	                         * observer.
	                         */
	                        return true;
	                    }
	                }
	                return false;
	            }
	        }
	
	        function patch(target, funcName) {
	            var base = target[funcName];
	            var mixinFunc = reactiveMixin[funcName];
	            if (!base) {
	                target[funcName] = mixinFunc;
	            } else {
	                target[funcName] = function() {
	                    base.apply(this, arguments);
	                    mixinFunc.apply(this, arguments);
	                }
	            }
	        }
	
	        function observer(componentClass) {
	            // If it is function but doesn't seem to be a react class constructor,
	            // wrap it to a react class automatically
	            if (
	                typeof componentClass === "function" &&
	                (!componentClass.prototype || !componentClass.prototype.render) &&
	                !componentClass.isReactClass && 
	                !React.Component.isPrototypeOf(componentClass)
	            ) {
	                return observer(React.createClass({
	                    displayName:     componentClass.displayName || componentClass.name,
	                    propTypes:       componentClass.propTypes,
	                    contextTypes:    componentClass.contextTypes,
	                    getDefaultProps: function() { return componentClass.defaultProps; },
	                    render:          function() { return componentClass.call(this, this.props, this.context); }
	                }));
	            }
	
	            if (!componentClass)
	                throw new Error("Please pass a valid component to 'observer'");
	            var target = componentClass.prototype || componentClass;
	
	            [
	                "componentWillMount",
	                "componentWillUnmount",
	                "componentDidMount",
	                "componentDidUpdate"
	            ].forEach(function(funcName) {
	                patch(target, funcName)
	            });
	
	            if (!target.shouldComponentUpdate)
	                target.shouldComponentUpdate = reactiveMixin.shouldComponentUpdate;
	            componentClass.isMobXReactObserver = true;
	            return componentClass;
	        }
	
	        function trackComponents() {
	            if (typeof WeakMap === "undefined")
	                throw new Error("[mobx-react] tracking components is not supported in this browser.");
	            if (!isDevtoolsEnabled)
	                isDevtoolsEnabled = true;
	        }
	
	        function EventEmitter() {
	            this.listeners = [];
	        };
	        EventEmitter.prototype.on = function (cb) {
	            this.listeners.push(cb);
	            var self = this;
	            return function() {
	                var idx = self.listeners.indexOf(cb);
	                if (idx !== -1)
	                    self.listeners.splice(idx, 1);
	            };
	        };
	        EventEmitter.prototype.emit = function(data) {
	            this.listeners.forEach(function (fn) {
	                fn(data);
	            });
	        };
	
	        return ({
	            observer: observer,
	            reactiveComponent: function() {
	                console.warn("[mobx-react] `reactiveComponent` has been renamed to `observer` and will be removed in 1.1.");
	                return observer.apply(null, arguments);
	            },
	            renderReporter: renderReporter,
	            componentByNodeRegistery: componentByNodeRegistery,
	            trackComponents: trackComponents
	        });
	    }
	
	    // UMD
	    if (true) {
	        module.exports = mrFactory(__webpack_require__(/*! mobx */ 169), __webpack_require__(/*! react */ 1), __webpack_require__(/*! react-dom */ 38));
	    } else if (typeof define === 'function' && define.amd) {
	        define('mobx-react', ['mobx', 'react', 'react-dom'], mrFactory);
	    } else {
	        this.mobxReact = mrFactory(this['mobx'], this['React'], this['ReactDOM']);
	    }
	})();


/***/ },

/***/ 169:
/*!****************************!*\
  !*** ./~/mobx/lib/mobx.js ***!
  \****************************/
/***/ function(module, exports) {

	/* WEBPACK VAR INJECTION */(function(global) {"use strict";
	var __extends = (this && this.__extends) || function (d, b) {
	    for (var p in b) if (b.hasOwnProperty(p)) d[p] = b[p];
	    function __() { this.constructor = d; }
	    d.prototype = b === null ? Object.create(b) : (__.prototype = b.prototype, new __());
	};
	registerGlobals();
	exports.extras = {
	    allowStateChanges: allowStateChanges,
	    getAtom: getAtom,
	    getDebugName: getDebugName,
	    getDependencyTree: getDependencyTree,
	    getObserverTree: getObserverTree,
	    isComputingDerivation: isComputingDerivation,
	    isSpyEnabled: isSpyEnabled,
	    resetGlobalState: resetGlobalState,
	    spyReport: spyReport,
	    spyReportEnd: spyReportEnd,
	    spyReportStart: spyReportStart,
	    trackTransitions: trackTransitions
	};
	exports._ = {
	    getAdministration: getAdministration,
	    quickDiff: quickDiff,
	    resetGlobalState: resetGlobalState
	};
	function autorun(arg1, arg2, arg3) {
	    var name, view, scope;
	    if (typeof arg1 === "string") {
	        name = arg1;
	        view = arg2;
	        scope = arg3;
	    }
	    else if (typeof arg1 === "function") {
	        name = arg1.name || ("Autorun@" + getNextId());
	        view = arg1;
	        scope = arg2;
	    }
	    assertUnwrapped(view, "autorun methods cannot have modifiers");
	    invariant(typeof view === "function", "autorun expects a function");
	    invariant(view.length === 0, "autorun expects a function without arguments");
	    if (scope)
	        view = view.bind(scope);
	    var reaction = new Reaction(name, function () {
	        this.track(view);
	    });
	    reaction.schedule();
	    return reaction.getDisposer();
	}
	exports.autorun = autorun;
	function when(arg1, arg2, arg3, arg4) {
	    var name, predicate, effect, scope;
	    if (typeof arg1 === "string") {
	        name = arg1;
	        predicate = arg2;
	        effect = arg3;
	        scope = arg4;
	    }
	    else if (typeof arg1 === "function") {
	        name = ("When@" + getNextId());
	        predicate = arg1;
	        effect = arg2;
	        scope = arg3;
	    }
	    var disposeImmediately = false;
	    var disposer = autorun(name, function () {
	        if (predicate.call(scope)) {
	            if (disposer)
	                disposer();
	            else
	                disposeImmediately = true;
	            var prevUntracked = untrackedStart();
	            effect.call(scope);
	            untrackedEnd(prevUntracked);
	        }
	    });
	    if (disposeImmediately)
	        disposer();
	    return disposer;
	}
	exports.when = when;
	function autorunUntil(predicate, effect, scope) {
	    deprecated("`autorunUntil` is deprecated, please use `when`.");
	    return when.apply(null, arguments);
	}
	exports.autorunUntil = autorunUntil;
	function autorunAsync(arg1, arg2, arg3, arg4) {
	    var name, func, delay, scope;
	    if (typeof arg1 === "string") {
	        name = arg1;
	        func = arg2;
	        delay = arg3;
	        scope = arg4;
	    }
	    else if (typeof arg1 === "function") {
	        name = arg1.name || ("AutorunAsync@" + getNextId());
	        func = arg1;
	        delay = arg2;
	        scope = arg3;
	    }
	    if (delay === void 0)
	        delay = 1;
	    if (scope)
	        func = func.bind(scope);
	    var isScheduled = false;
	    var r = new Reaction(name, function () {
	        if (!isScheduled) {
	            isScheduled = true;
	            setTimeout(function () {
	                isScheduled = false;
	                if (!r.isDisposed)
	                    r.track(func);
	            }, delay);
	        }
	    });
	    r.schedule();
	    return r.getDisposer();
	}
	exports.autorunAsync = autorunAsync;
	function reaction(arg1, arg2, arg3, arg4, arg5, arg6) {
	    var name, expression, effect, fireImmediately, delay, scope;
	    if (typeof arg1 === "string") {
	        name = arg1;
	        expression = arg2;
	        effect = arg3;
	        fireImmediately = arg4;
	        delay = arg5;
	        scope = arg6;
	    }
	    else {
	        name = arg1.name || arg2.name || ("Reaction@" + getNextId());
	        expression = arg1;
	        effect = arg2;
	        fireImmediately = arg3;
	        delay = arg4;
	        scope = arg5;
	    }
	    if (fireImmediately === void 0)
	        fireImmediately = false;
	    if (delay === void 0)
	        delay = 0;
	    var _a = getValueModeFromValue(expression, ValueMode.Reference), valueMode = _a[0], unwrappedExpression = _a[1];
	    var compareStructural = valueMode === ValueMode.Structure;
	    if (scope) {
	        unwrappedExpression = unwrappedExpression.bind(scope);
	        effect = action(name, effect.bind(scope));
	    }
	    var firstTime = true;
	    var isScheduled = false;
	    var nextValue = undefined;
	    function reactionRunner() {
	        if (r.isDisposed)
	            return;
	        var changed = false;
	        r.track(function () {
	            var v = unwrappedExpression();
	            changed = valueDidChange(compareStructural, nextValue, v);
	            nextValue = v;
	        });
	        if (firstTime && fireImmediately)
	            effect(nextValue);
	        if (!firstTime && changed === true)
	            effect(nextValue);
	        if (firstTime)
	            firstTime = false;
	    }
	    var r = new Reaction(name, function () {
	        if (delay < 1) {
	            reactionRunner();
	        }
	        else if (!isScheduled) {
	            isScheduled = true;
	            setTimeout(function () {
	                isScheduled = false;
	                reactionRunner();
	            }, delay);
	        }
	    });
	    r.schedule();
	    return r.getDisposer();
	}
	exports.reaction = reaction;
	var computedDecorator = createClassPropertyDecorator(function (target, name, _, decoratorArgs, originalDescriptor) {
	    var baseValue = originalDescriptor.get;
	    invariant(typeof baseValue === "function", "@computed can only be used on getter functions, like: '@computed get myProps() { return ...; }'");
	    var compareStructural = false;
	    if (decoratorArgs && decoratorArgs.length === 1 && decoratorArgs[0].asStructure === true)
	        compareStructural = true;
	    var adm = asObservableObject(target, undefined, ValueMode.Recursive);
	    defineObservableProperty(adm, name, compareStructural ? asStructure(baseValue) : baseValue, false);
	}, function (name) {
	    return this.$mobx.values[name].get();
	}, throwingComputedValueSetter, false, true);
	function computed(targetOrExpr, keyOrScope, baseDescriptor, options) {
	    if (arguments.length < 3 && typeof targetOrExpr === "function")
	        return computedExpr(targetOrExpr, keyOrScope);
	    invariant(!baseDescriptor || !baseDescriptor.set, "@observable properties cannot have a setter: " + keyOrScope);
	    return computedDecorator.apply(null, arguments);
	}
	exports.computed = computed;
	function computedExpr(expr, scope) {
	    var _a = getValueModeFromValue(expr, ValueMode.Recursive), mode = _a[0], value = _a[1];
	    return new ComputedValue(value, scope, mode === ValueMode.Structure, value.name);
	}
	function throwingComputedValueSetter() {
	    throw new Error("[ComputedValue] It is not allowed to assign new values to computed properties.");
	}
	function createTransformer(transformer, onCleanup) {
	    invariant(typeof transformer === "function" && transformer.length === 1, "createTransformer expects a function that accepts one argument");
	    var objectCache = {};
	    var resetId = globalState.resetId;
	    var Transformer = (function (_super) {
	        __extends(Transformer, _super);
	        function Transformer(sourceIdentifier, sourceObject) {
	            _super.call(this, function () { return transformer(sourceObject); }, null, false, "Transformer-" + transformer.name + "-" + sourceIdentifier);
	            this.sourceIdentifier = sourceIdentifier;
	            this.sourceObject = sourceObject;
	        }
	        Transformer.prototype.onBecomeUnobserved = function () {
	            var lastValue = this.value;
	            _super.prototype.onBecomeUnobserved.call(this);
	            delete objectCache[this.sourceIdentifier];
	            if (onCleanup)
	                onCleanup(lastValue, this.sourceObject);
	        };
	        return Transformer;
	    }(ComputedValue));
	    return function (object) {
	        if (resetId !== globalState.resetId) {
	            objectCache = {};
	            resetId = globalState.resetId;
	        }
	        var identifier = getMemoizationId(object);
	        var reactiveTransformer = objectCache[identifier];
	        if (reactiveTransformer)
	            return reactiveTransformer.get();
	        reactiveTransformer = objectCache[identifier] = new Transformer(identifier, object);
	        return reactiveTransformer.get();
	    };
	}
	exports.createTransformer = createTransformer;
	function getMemoizationId(object) {
	    if (object === null || typeof object !== "object")
	        throw new Error("[mobx] transform expected some kind of object, got: " + object);
	    var tid = object.$transformId;
	    if (tid === undefined) {
	        tid = getNextId();
	        Object.defineProperty(object, "$transformId", {
	            configurable: true,
	            writable: true,
	            enumerable: false,
	            value: tid
	        });
	    }
	    return tid;
	}
	function expr(expr, scope) {
	    if (!isComputingDerivation())
	        console.warn("[mobx.expr] 'expr' should only be used inside other reactive functions.");
	    return computed(expr, scope).get();
	}
	exports.expr = expr;
	function extendObservable(target) {
	    var properties = [];
	    for (var _i = 1; _i < arguments.length; _i++) {
	        properties[_i - 1] = arguments[_i];
	    }
	    invariant(arguments.length >= 2, "extendObservable expected 2 or more arguments");
	    invariant(typeof target === "object", "extendObservable expects an object as first argument");
	    invariant(!(target instanceof ObservableMap), "extendObservable should not be used on maps, use map.merge instead");
	    properties.forEach(function (propSet) {
	        invariant(typeof propSet === "object", "all arguments of extendObservable should be objects");
	        extendObservableHelper(target, propSet, ValueMode.Recursive, null);
	    });
	    return target;
	}
	exports.extendObservable = extendObservable;
	function extendObservableHelper(target, properties, mode, name) {
	    var adm = asObservableObject(target, name, mode);
	    for (var key in properties)
	        if (properties.hasOwnProperty(key)) {
	            if (target === properties && !isPropertyConfigurable(target, key))
	                continue;
	            setObservableObjectInstanceProperty(adm, key, properties[key]);
	        }
	    return target;
	}
	function getDependencyTree(thing, property) {
	    return nodeToDependencyTree(getAtom(thing, property));
	}
	function nodeToDependencyTree(node) {
	    var result = {
	        name: node.name
	    };
	    if (node.observing && node.observing.length)
	        result.dependencies = unique(node.observing).map(nodeToDependencyTree);
	    return result;
	}
	function getObserverTree(thing, property) {
	    return nodeToObserverTree(getAtom(thing, property));
	}
	function nodeToObserverTree(node) {
	    var result = {
	        name: node.name
	    };
	    if (node.observers && node.observers.length)
	        result.observers = unique(node.observers).map(nodeToObserverTree);
	    return result;
	}
	function intercept(thing, propOrHandler, handler) {
	    if (typeof handler === "function")
	        return interceptProperty(thing, propOrHandler, handler);
	    else
	        return interceptInterceptable(thing, propOrHandler);
	}
	exports.intercept = intercept;
	function interceptInterceptable(thing, handler) {
	    if (isPlainObject(thing) && !isObservableObject(thing)) {
	        deprecated("Passing plain objects to intercept / observe is deprecated and will be removed in 3.0");
	        return getAdministration(observable(thing)).intercept(handler);
	    }
	    return getAdministration(thing).intercept(handler);
	}
	function interceptProperty(thing, property, handler) {
	    if (isPlainObject(thing) && !isObservableObject(thing)) {
	        deprecated("Passing plain objects to intercept / observe is deprecated and will be removed in 3.0");
	        extendObservable(thing, {
	            property: thing[property]
	        });
	        return interceptProperty(thing, property, handler);
	    }
	    return getAdministration(thing, property).intercept(handler);
	}
	function isObservable(value, property) {
	    if (value === null || value === undefined)
	        return false;
	    if (property !== undefined) {
	        if (value instanceof ObservableMap || value instanceof ObservableArray)
	            throw new Error("[mobx.isObservable] isObservable(object, propertyName) is not supported for arrays and maps. Use map.has or array.length instead.");
	        else if (isObservableObject(value)) {
	            var o = value.$mobx;
	            return o.values && !!o.values[property];
	        }
	        return false;
	    }
	    return !!value.$mobx || value instanceof Atom || value instanceof Reaction || value instanceof ComputedValue;
	}
	exports.isObservable = isObservable;
	var decoratorImpl = createClassPropertyDecorator(function (target, name, baseValue) {
	    var prevA = allowStateChangesStart(true);
	    if (typeof baseValue === "function")
	        baseValue = asReference(baseValue);
	    var adm = asObservableObject(target, undefined, ValueMode.Recursive);
	    defineObservableProperty(adm, name, baseValue, false);
	    allowStateChangesEnd(prevA);
	}, function (name) {
	    return this.$mobx.values[name].get();
	}, function (name, value) {
	    setPropertyValue(this, name, value);
	}, true, false);
	function observableDecorator(target, key, baseDescriptor) {
	    invariant(arguments.length >= 2 && arguments.length <= 3, "Illegal decorator config", key);
	    assertPropertyConfigurable(target, key);
	    invariant(!baseDescriptor || !baseDescriptor.get, "@observable can not be used on getters, use @computed instead");
	    return decoratorImpl.apply(null, arguments);
	}
	function observable(v, keyOrScope) {
	    if (v === void 0) { v = undefined; }
	    if (typeof arguments[1] === "string")
	        return observableDecorator.apply(null, arguments);
	    invariant(arguments.length < 3, "observable expects zero, one or two arguments");
	    if (isObservable(v))
	        return v;
	    var _a = getValueModeFromValue(v, ValueMode.Recursive), mode = _a[0], value = _a[1];
	    var sourceType = mode === ValueMode.Reference ? ValueType.Reference : getTypeOfValue(value);
	    switch (sourceType) {
	        case ValueType.Array:
	        case ValueType.PlainObject:
	            return makeChildObservable(value, mode);
	        case ValueType.Reference:
	        case ValueType.ComplexObject:
	            return new ObservableValue(value, mode);
	        case ValueType.ComplexFunction:
	            throw new Error("[mobx.observable] To be able to make a function reactive it should not have arguments. If you need an observable reference to a function, use `observable(asReference(f))`");
	        case ValueType.ViewFunction:
	            deprecated("Use `computed(expr)` instead of `observable(expr)`");
	            return computed(v, keyOrScope);
	    }
	    invariant(false, "Illegal State");
	}
	exports.observable = observable;
	var ValueType;
	(function (ValueType) {
	    ValueType[ValueType["Reference"] = 0] = "Reference";
	    ValueType[ValueType["PlainObject"] = 1] = "PlainObject";
	    ValueType[ValueType["ComplexObject"] = 2] = "ComplexObject";
	    ValueType[ValueType["Array"] = 3] = "Array";
	    ValueType[ValueType["ViewFunction"] = 4] = "ViewFunction";
	    ValueType[ValueType["ComplexFunction"] = 5] = "ComplexFunction";
	})(ValueType || (ValueType = {}));
	function getTypeOfValue(value) {
	    if (value === null || value === undefined)
	        return ValueType.Reference;
	    if (typeof value === "function")
	        return value.length ? ValueType.ComplexFunction : ValueType.ViewFunction;
	    if (Array.isArray(value) || value instanceof ObservableArray)
	        return ValueType.Array;
	    if (typeof value === "object")
	        return isPlainObject(value) ? ValueType.PlainObject : ValueType.ComplexObject;
	    return ValueType.Reference;
	}
	function observe(thing, propOrCb, cbOrFire, fireImmediately) {
	    if (typeof cbOrFire === "function")
	        return observeObservableProperty(thing, propOrCb, cbOrFire, fireImmediately);
	    else
	        return observeObservable(thing, propOrCb, cbOrFire);
	}
	exports.observe = observe;
	function observeObservable(thing, listener, fireImmediately) {
	    if (isPlainObject(thing) && !isObservableObject(thing)) {
	        deprecated("Passing plain objects to intercept / observe is deprecated and will be removed in 3.0");
	        return getAdministration(observable(thing)).observe(listener, fireImmediately);
	    }
	    return getAdministration(thing).observe(listener, fireImmediately);
	}
	function observeObservableProperty(thing, property, listener, fireImmediately) {
	    if (isPlainObject(thing) && !isObservableObject(thing)) {
	        deprecated("Passing plain objects to intercept / observe is deprecated and will be removed in 3.0");
	        extendObservable(thing, {
	            property: thing[property]
	        });
	        return observeObservableProperty(thing, property, listener, fireImmediately);
	    }
	    return getAdministration(thing, property).observe(listener, fireImmediately);
	}
	function toJS(source, detectCycles, __alreadySeen) {
	    if (detectCycles === void 0) { detectCycles = true; }
	    if (__alreadySeen === void 0) { __alreadySeen = null; }
	    function cache(value) {
	        if (detectCycles)
	            __alreadySeen.push([source, value]);
	        return value;
	    }
	    if (detectCycles && __alreadySeen === null)
	        __alreadySeen = [];
	    if (detectCycles && source !== null && typeof source === "object") {
	        for (var i = 0, l = __alreadySeen.length; i < l; i++)
	            if (__alreadySeen[i][0] === source)
	                return __alreadySeen[i][1];
	    }
	    if (!source)
	        return source;
	    if (Array.isArray(source) || source instanceof ObservableArray) {
	        var res = cache([]);
	        var toAdd = source.map(function (value) { return toJS(value, detectCycles, __alreadySeen); });
	        res.length = toAdd.length;
	        for (var i = 0, l = toAdd.length; i < l; i++)
	            res[i] = toAdd[i];
	        return res;
	    }
	    if (source instanceof ObservableMap) {
	        var res_1 = cache({});
	        source.forEach(function (value, key) { return res_1[key] = toJS(value, detectCycles, __alreadySeen); });
	        return res_1;
	    }
	    if (isObservable(source) && source.$mobx instanceof ObservableValue)
	        return toJS(source(), detectCycles, __alreadySeen);
	    if (source instanceof ObservableValue)
	        return toJS(source.get(), detectCycles, __alreadySeen);
	    if (typeof source === "object") {
	        var res = cache({});
	        for (var key in source)
	            res[key] = toJS(source[key], detectCycles, __alreadySeen);
	        return res;
	    }
	    return source;
	}
	exports.toJS = toJS;
	function toJSON(source, detectCycles, __alreadySeen) {
	    if (detectCycles === void 0) { detectCycles = true; }
	    if (__alreadySeen === void 0) { __alreadySeen = null; }
	    deprecated("toJSON is deprecated. Use toJS instead");
	    return toJS.apply(null, arguments);
	}
	exports.toJSON = toJSON;
	function log(msg) {
	    console.log(msg);
	    return msg;
	}
	function whyRun(thing, prop) {
	    switch (arguments.length) {
	        case 0:
	            thing = globalState.derivationStack[globalState.derivationStack.length - 1];
	            if (!thing)
	                return log("whyRun() can only be used if a derivation is active, or by passing an computed value / reaction explicitly. If you invoked whyRun from inside a computation; the computation is currently suspended but re-evaluating because somebody requested it's value.");
	            break;
	        case 2:
	            thing = getAtom(thing, prop);
	            break;
	    }
	    thing = getAtom(thing);
	    if (thing instanceof ComputedValue)
	        return log(thing.whyRun());
	    else if (thing instanceof Reaction)
	        return log(thing.whyRun());
	    else
	        invariant(false, "whyRun can only be used on reactions and computed values");
	}
	exports.whyRun = whyRun;
	var actionDecorator = createClassPropertyDecorator(function (target, key, value, args, originalDescriptor) {
	    var actionName = (args && args.length === 1) ? args[0] : (value.name || key || "<unnamed action>");
	    var wrappedAction = action(actionName, value);
	    if (originalDescriptor && originalDescriptor.value && target.constructor && target.constructor.prototype) {
	        Object.defineProperty(target.constructor.prototype, key, {
	            configurable: true, enumerable: false, writable: false,
	            value: wrappedAction
	        });
	    }
	    else {
	        Object.defineProperty(target, key, {
	            configurable: true, enumerable: false, writable: false,
	            value: wrappedAction
	        });
	    }
	}, function (key) {
	    return this[key];
	}, function () {
	    invariant(false, "It is not allowed to assign new values to @action fields");
	}, false, true);
	function action(arg1, arg2, arg3, arg4) {
	    if (arguments.length === 1 && typeof arg1 === "function")
	        return actionImplementation(arg1.name || "<unnamed action>", arg1);
	    if (arguments.length === 2 && typeof arg2 === "function")
	        return actionImplementation(arg1, arg2);
	    return actionDecorator.apply(null, arguments);
	}
	exports.action = action;
	function isAction(thing) {
	    return typeof thing === "function" && thing.isMobxAction === true;
	}
	exports.isAction = isAction;
	function runInAction(arg1, arg2, arg3) {
	    var actionName = typeof arg1 === "string" ? arg1 : arg1.name || "<unnamed action>";
	    var fn = typeof arg1 === "function" ? arg1 : arg2;
	    var scope = typeof arg1 === "function" ? arg2 : arg3;
	    invariant(typeof fn === "function", "`runInAction` expects a function");
	    invariant(fn.length === 0, "`runInAction` expects a function without arguments");
	    invariant(typeof actionName === "string" && actionName.length > 0, "actions should have valid names, got: '" + actionName + "'");
	    return executeWrapped(actionName, fn, scope, undefined);
	}
	exports.runInAction = runInAction;
	function actionImplementation(actionName, fn) {
	    invariant(typeof fn === "function", "`action` can only be invoked on functions");
	    invariant(typeof actionName === "string" && actionName.length > 0, "actions should have valid names, got: '" + actionName + "'");
	    var res = function () {
	        return executeWrapped(actionName, fn, this, arguments);
	    };
	    res.isMobxAction = true;
	    return res;
	}
	function executeWrapped(actionName, fn, scope, args) {
	    var ds = globalState.derivationStack;
	    invariant(!(ds[ds.length - 1] instanceof ComputedValue), "Computed values or transformers should not invoke actions or trigger other side effects");
	    var notifySpy = isSpyEnabled();
	    var startTime;
	    if (notifySpy) {
	        startTime = Date.now();
	        var flattendArgs = [];
	        for (var i = 0, l = args.length; i < l; i++)
	            flattendArgs.push(args[i]);
	        spyReportStart({
	            type: "action",
	            name: actionName,
	            fn: fn,
	            target: scope,
	            arguments: flattendArgs
	        });
	    }
	    var prevUntracked = untrackedStart();
	    transactionStart(actionName, scope, false);
	    var prevAllowStateChanges = allowStateChangesStart(true);
	    try {
	        return fn.apply(scope, args);
	    }
	    finally {
	        allowStateChangesEnd(prevAllowStateChanges);
	        transactionEnd(false);
	        untrackedEnd(prevUntracked);
	        if (notifySpy)
	            spyReportEnd({ time: Date.now() - startTime });
	    }
	}
	function useStrict(strict) {
	    invariant(globalState.derivationStack.length === 0, "It is not allowed to set `useStrict` when a derivation is running");
	    globalState.strictMode = strict;
	    globalState.allowStateChanges = !strict;
	}
	exports.useStrict = useStrict;
	function allowStateChanges(allowStateChanges, func) {
	    var prev = allowStateChangesStart(allowStateChanges);
	    var res = func();
	    allowStateChangesEnd(prev);
	    return res;
	}
	function allowStateChangesStart(allowStateChanges) {
	    var prev = globalState.allowStateChanges;
	    globalState.allowStateChanges = allowStateChanges;
	    return prev;
	}
	function allowStateChangesEnd(prev) {
	    globalState.allowStateChanges = prev;
	}
	function propagateAtomReady(atom) {
	    invariant(atom.isDirty, "atom not dirty");
	    atom.isDirty = false;
	    propagateReadiness(atom, true);
	}
	var Atom = (function () {
	    function Atom(name, onBecomeObserved, onBecomeUnobserved) {
	        if (name === void 0) { name = "Atom@" + getNextId(); }
	        if (onBecomeObserved === void 0) { onBecomeObserved = noop; }
	        if (onBecomeUnobserved === void 0) { onBecomeUnobserved = noop; }
	        this.name = name;
	        this.onBecomeObserved = onBecomeObserved;
	        this.onBecomeUnobserved = onBecomeUnobserved;
	        this.isDirty = false;
	        this.staleObservers = [];
	        this.observers = [];
	    }
	    Atom.prototype.reportObserved = function () {
	        reportObserved(this);
	    };
	    Atom.prototype.reportChanged = function () {
	        if (!this.isDirty) {
	            this.reportStale();
	            this.reportReady();
	        }
	    };
	    Atom.prototype.reportStale = function () {
	        if (!this.isDirty) {
	            this.isDirty = true;
	            propagateStaleness(this);
	        }
	    };
	    Atom.prototype.reportReady = function () {
	        invariant(this.isDirty, "atom not dirty");
	        if (globalState.inTransaction > 0)
	            globalState.changedAtoms.push(this);
	        else {
	            propagateAtomReady(this);
	            runReactions();
	        }
	    };
	    Atom.prototype.toString = function () {
	        return this.name;
	    };
	    return Atom;
	}());
	exports.Atom = Atom;
	var ComputedValue = (function () {
	    function ComputedValue(derivation, scope, compareStructural, name) {
	        this.derivation = derivation;
	        this.scope = scope;
	        this.compareStructural = compareStructural;
	        this.isLazy = true;
	        this.isComputing = false;
	        this.staleObservers = [];
	        this.observers = [];
	        this.observing = [];
	        this.dependencyChangeCount = 0;
	        this.dependencyStaleCount = 0;
	        this.value = undefined;
	        this.name = name || "ComputedValue@" + getNextId();
	    }
	    ComputedValue.prototype.peek = function () {
	        this.isComputing = true;
	        var prevAllowStateChanges = allowStateChangesStart(false);
	        var res = this.derivation.call(this.scope);
	        allowStateChangesEnd(prevAllowStateChanges);
	        this.isComputing = false;
	        return res;
	    };
	    ;
	    ComputedValue.prototype.onBecomeObserved = function () {
	    };
	    ComputedValue.prototype.onBecomeUnobserved = function () {
	        for (var i = 0, l = this.observing.length; i < l; i++)
	            removeObserver(this.observing[i], this);
	        this.observing = [];
	        this.isLazy = true;
	        this.value = undefined;
	    };
	    ComputedValue.prototype.onDependenciesReady = function () {
	        var changed = this.trackAndCompute();
	        return changed;
	    };
	    ComputedValue.prototype.get = function () {
	        invariant(!this.isComputing, "Cycle detected", this.derivation);
	        reportObserved(this);
	        if (this.dependencyStaleCount > 0) {
	            return this.peek();
	        }
	        if (this.isLazy) {
	            if (isComputingDerivation()) {
	                this.isLazy = false;
	                this.trackAndCompute();
	            }
	            else {
	                return this.peek();
	            }
	        }
	        return this.value;
	    };
	    ComputedValue.prototype.set = function (_) {
	        throw new Error("[ComputedValue '" + name + "'] It is not possible to assign a new value to a computed value.");
	    };
	    ComputedValue.prototype.trackAndCompute = function () {
	        if (isSpyEnabled()) {
	            spyReport({
	                object: this,
	                type: "compute",
	                fn: this.derivation,
	                target: this.scope
	            });
	        }
	        var oldValue = this.value;
	        var newValue = this.value = trackDerivedFunction(this, this.peek);
	        return valueDidChange(this.compareStructural, newValue, oldValue);
	    };
	    ComputedValue.prototype.observe = function (listener, fireImmediately) {
	        var _this = this;
	        var firstTime = true;
	        var prevValue = undefined;
	        return autorun(function () {
	            var newValue = _this.get();
	            if (!firstTime || fireImmediately) {
	                var prevU = untrackedStart();
	                listener(newValue, prevValue);
	                untrackedEnd(prevU);
	            }
	            firstTime = false;
	            prevValue = newValue;
	        });
	    };
	    ComputedValue.prototype.toJSON = function () {
	        return this.get();
	    };
	    ComputedValue.prototype.toString = function () {
	        return this.name + "[" + this.derivation.toString() + "]";
	    };
	    ComputedValue.prototype.whyRun = function () {
	        var isTracking = globalState.derivationStack.length > 0;
	        var observing = unique(this.observing).map(function (dep) { return dep.name; });
	        var observers = unique(this.observers).map(function (dep) { return dep.name; });
	        var runReason = (this.isComputing
	            ? isTracking
	                ? this.observers.length > 0
	                    ? RunReason.INVALIDATED
	                    : RunReason.REQUIRED
	                : RunReason.PEEK
	            : RunReason.NOT_RUNNING);
	        if (runReason === RunReason.REQUIRED) {
	            var requiredBy = globalState.derivationStack[globalState.derivationStack.length - 2];
	            if (requiredBy)
	                observers.push(requiredBy.name);
	        }
	        return (("\nWhyRun? computation '" + this.name + "':\n * Running because: " + runReasonTexts[runReason] + " " + ((runReason === RunReason.NOT_RUNNING) && this.dependencyStaleCount > 0 ? "(a next run is scheduled)" : "") + "\n") +
	            (this.isLazy
	                ?
	                    " * This computation is suspended (not in use by any reaction) and won't run automatically.\n\tDidn't expect this computation to be suspended at this point?\n\t  1. Make sure this computation is used by a reaction (reaction, autorun, observer).\n\t  2. Check whether you are using this computation synchronously (in the same stack as they reaction that needs it).\n"
	                :
	                    " * This computation will re-run if any of the following observables changes:\n    " + joinStrings(observing) + "\n    " + ((this.isComputing && isTracking) ? " (... or any observable accessed during the remainder of the current run)" : "") + "\n\tMissing items in this list?\n\t  1. Check whether all used values are properly marked as observable (use isObservable to verify)\n\t  2. Make sure you didn't dereference values too early. MobX observes props, not primitives. E.g: use 'person.name' instead of 'name' in your computation.\n  * If the outcome of this computation changes, the following observers will be re-run:\n    " + joinStrings(observers) + "\n"));
	    };
	    return ComputedValue;
	}());
	var RunReason;
	(function (RunReason) {
	    RunReason[RunReason["PEEK"] = 0] = "PEEK";
	    RunReason[RunReason["INVALIDATED"] = 1] = "INVALIDATED";
	    RunReason[RunReason["REQUIRED"] = 2] = "REQUIRED";
	    RunReason[RunReason["NOT_RUNNING"] = 3] = "NOT_RUNNING";
	})(RunReason || (RunReason = {}));
	var runReasonTexts = (_a = {},
	    _a[RunReason.PEEK] = "[peek] The value of this computed value was requested outside an reaction",
	    _a[RunReason.INVALIDATED] = "[invalidated] Some observables used by this computation did change",
	    _a[RunReason.REQUIRED] = "[started] This computation is required by another computed value / reaction",
	    _a[RunReason.NOT_RUNNING] = "[idle] This compution is currently not running",
	    _a
	);
	function isComputingDerivation() {
	    return globalState.derivationStack.length > 0;
	}
	function checkIfStateModificationsAreAllowed() {
	    if (!globalState.allowStateChanges) {
	        invariant(false, globalState.strictMode
	            ? "It is not allowed to create or change state outside an `action` when MobX is in strict mode. Wrap the current method in `action` if this state change is intended"
	            : "It is not allowed to change the state when a computed value or transformer is being evaluated. Use 'autorun' to create reactive functions with side-effects.");
	    }
	}
	function notifyDependencyStale(derivation) {
	    if (++derivation.dependencyStaleCount === 1) {
	        propagateStaleness(derivation);
	    }
	}
	function notifyDependencyReady(derivation, dependencyDidChange) {
	    invariant(derivation.dependencyStaleCount > 0, "unexpected ready notification");
	    if (dependencyDidChange)
	        derivation.dependencyChangeCount += 1;
	    if (--derivation.dependencyStaleCount === 0) {
	        if (derivation.dependencyChangeCount > 0) {
	            derivation.dependencyChangeCount = 0;
	            var changed = derivation.onDependenciesReady();
	            propagateReadiness(derivation, changed);
	        }
	        else {
	            propagateReadiness(derivation, false);
	        }
	    }
	}
	function trackDerivedFunction(derivation, f) {
	    var hasException = true;
	    var prevObserving = derivation.observing;
	    derivation.observing = [];
	    globalState.derivationStack.push(derivation);
	    var prevTracking = globalState.isTracking;
	    globalState.isTracking = true;
	    try {
	        var result = f.call(derivation);
	        hasException = false;
	        bindDependencies(derivation, prevObserving);
	        globalState.isTracking = prevTracking;
	        return result;
	    }
	    finally {
	        if (hasException) {
	            var message = ("[mobx] An uncaught exception occurred while calculating your computed value, autorun or transformer. Or inside the render() method of an observer based React component. " +
	                "These methods should never throw exceptions as MobX will usually not be able to recover from them. " +
	                ("Please enable 'Pause on (caught) exceptions' in your debugger to find the root cause. In: '" + derivation.name + "'"));
	            if (isSpyEnabled()) {
	                spyReport({
	                    type: "error",
	                    object: this,
	                    message: message
	                });
	            }
	            console.error(message);
	            resetGlobalState();
	        }
	    }
	}
	function bindDependencies(derivation, prevObserving) {
	    globalState.derivationStack.length -= 1;
	    var _a = quickDiff(derivation.observing, prevObserving), added = _a[0], removed = _a[1];
	    for (var i = 0, l = added.length; i < l; i++) {
	        var dependency = added[i];
	        invariant(!findCycle(derivation, dependency), "Cycle detected", derivation);
	        addObserver(added[i], derivation);
	    }
	    for (var i = 0, l = removed.length; i < l; i++)
	        removeObserver(removed[i], derivation);
	}
	function findCycle(needle, node) {
	    if (needle === node)
	        return true;
	    var obs = node.observing;
	    if (obs === undefined)
	        return false;
	    for (var l = obs.length, i = 0; i < l; i++)
	        if (findCycle(needle, obs[i]))
	            return true;
	    return false;
	}
	function untracked(action) {
	    var prev = untrackedStart();
	    var res = action();
	    untrackedEnd(prev);
	    return res;
	}
	exports.untracked = untracked;
	function untrackedStart() {
	    var prev = globalState.isTracking;
	    globalState.isTracking = false;
	    return prev;
	}
	function untrackedEnd(prev) {
	    globalState.isTracking = prev;
	}
	var persistentKeys = ["mobxGuid", "resetId", "spyListeners", "strictMode"];
	var MobXGlobals = (function () {
	    function MobXGlobals() {
	        this.version = 2;
	        this.derivationStack = [];
	        this.mobxGuid = 0;
	        this.inTransaction = 0;
	        this.isTracking = false;
	        this.isRunningReactions = false;
	        this.changedAtoms = [];
	        this.pendingReactions = [];
	        this.allowStateChanges = true;
	        this.strictMode = false;
	        this.resetId = 0;
	        this.spyListeners = [];
	    }
	    return MobXGlobals;
	}());
	var globalState = (function () {
	    var res = new MobXGlobals();
	    if (global.__mobservableTrackingStack || global.__mobservableViewStack)
	        throw new Error("[mobx] An incompatible version of mobservable is already loaded.");
	    if (global.__mobxGlobal && global.__mobxGlobal.version !== res.version)
	        throw new Error("[mobx] An incompatible version of mobx is already loaded.");
	    if (global.__mobxGlobal)
	        return global.__mobxGlobal;
	    return global.__mobxGlobal = res;
	})();
	function registerGlobals() {
	}
	function resetGlobalState() {
	    globalState.resetId++;
	    var defaultGlobals = new MobXGlobals();
	    for (var key in defaultGlobals)
	        if (persistentKeys.indexOf(key) === -1)
	            globalState[key] = defaultGlobals[key];
	    globalState.allowStateChanges = !globalState.strictMode;
	}
	function addObserver(observable, node) {
	    var obs = observable.observers, l = obs.length;
	    obs[l] = node;
	    if (l === 0)
	        observable.onBecomeObserved();
	}
	function removeObserver(observable, node) {
	    var obs = observable.observers, idx = obs.indexOf(node);
	    if (idx !== -1)
	        obs.splice(idx, 1);
	    if (obs.length === 0)
	        observable.onBecomeUnobserved();
	}
	function reportObserved(observable) {
	    if (globalState.isTracking === false)
	        return;
	    var derivationStack = globalState.derivationStack;
	    var deps = derivationStack[derivationStack.length - 1].observing;
	    var depslength = deps.length;
	    if (deps[depslength - 1] !== observable && deps[depslength - 2] !== observable)
	        deps[depslength] = observable;
	}
	function propagateStaleness(observable) {
	    var os = observable.observers.slice();
	    os.forEach(notifyDependencyStale);
	    observable.staleObservers = observable.staleObservers.concat(os);
	}
	function propagateReadiness(observable, valueDidActuallyChange) {
	    observable.staleObservers.splice(0).forEach(function (o) { return notifyDependencyReady(o, valueDidActuallyChange); });
	}
	var Reaction = (function () {
	    function Reaction(name, onInvalidate) {
	        if (name === void 0) { name = "Reaction@" + getNextId(); }
	        this.name = name;
	        this.onInvalidate = onInvalidate;
	        this.staleObservers = EMPTY_ARRAY;
	        this.observers = EMPTY_ARRAY;
	        this.observing = [];
	        this.dependencyChangeCount = 0;
	        this.dependencyStaleCount = 0;
	        this.isDisposed = false;
	        this._isScheduled = false;
	        this._isTrackPending = false;
	        this._isRunning = false;
	    }
	    Reaction.prototype.onBecomeObserved = function () {
	    };
	    Reaction.prototype.onBecomeUnobserved = function () {
	    };
	    Reaction.prototype.onDependenciesReady = function () {
	        this.schedule();
	        return false;
	    };
	    Reaction.prototype.schedule = function () {
	        if (!this._isScheduled) {
	            this._isScheduled = true;
	            globalState.pendingReactions.push(this);
	            runReactions();
	        }
	    };
	    Reaction.prototype.isScheduled = function () {
	        return this.dependencyStaleCount > 0 || this._isScheduled;
	    };
	    Reaction.prototype.runReaction = function () {
	        if (!this.isDisposed) {
	            this._isScheduled = false;
	            this._isTrackPending = true;
	            this.onInvalidate();
	            if (this._isTrackPending && isSpyEnabled()) {
	                spyReport({
	                    object: this,
	                    type: "scheduled-reaction"
	                });
	            }
	        }
	    };
	    Reaction.prototype.track = function (fn) {
	        var notify = isSpyEnabled();
	        var startTime;
	        if (notify) {
	            startTime = Date.now();
	            spyReportStart({
	                object: this,
	                type: "reaction",
	                fn: fn
	            });
	        }
	        this._isRunning = true;
	        trackDerivedFunction(this, fn);
	        this._isRunning = false;
	        this._isTrackPending = false;
	        if (notify) {
	            spyReportEnd({
	                time: Date.now() - startTime
	            });
	        }
	    };
	    Reaction.prototype.dispose = function () {
	        if (!this.isDisposed) {
	            this.isDisposed = true;
	            var deps = this.observing.splice(0);
	            for (var i = 0, l = deps.length; i < l; i++)
	                removeObserver(deps[i], this);
	        }
	    };
	    Reaction.prototype.getDisposer = function () {
	        var r = this.dispose.bind(this);
	        r.$mobx = this;
	        return r;
	    };
	    Reaction.prototype.toString = function () {
	        return "Reaction[" + this.name + "]";
	    };
	    Reaction.prototype.whyRun = function () {
	        var observing = unique(this.observing).map(function (dep) { return dep.name; });
	        return ("\nWhyRun? reaction '" + this.name + "':\n * Status: [" + (this.isDisposed ? "stopped" : this._isRunning ? "running" : this.isScheduled() ? "scheduled" : "idle") + "]\n * This reaction will re-run if any of the following observables changes:\n    " + joinStrings(observing) + "\n    " + ((this._isRunning) ? " (... or any observable accessed during the remainder of the current run)" : "") + "\n\tMissing items in this list?\n\t  1. Check whether all used values are properly marked as observable (use isObservable to verify)\n\t  2. Make sure you didn't dereference values too early. MobX observes props, not primitives. E.g: use 'person.name' instead of 'name' in your computation.\n");
	    };
	    return Reaction;
	}());
	exports.Reaction = Reaction;
	var MAX_REACTION_ITERATIONS = 100;
	function runReactions() {
	    if (globalState.isRunningReactions === true || globalState.inTransaction > 0)
	        return;
	    globalState.isRunningReactions = true;
	    var allReactions = globalState.pendingReactions;
	    var iterations = 0;
	    while (allReactions.length > 0) {
	        if (++iterations === MAX_REACTION_ITERATIONS)
	            throw new Error("Reaction doesn't converge to a stable state. Probably there is a cycle in the reactive function: " + allReactions[0].toString());
	        var remainingReactions = allReactions.splice(0);
	        for (var i = 0, l = remainingReactions.length; i < l; i++)
	            remainingReactions[i].runReaction();
	    }
	    globalState.isRunningReactions = false;
	}
	var spyEnabled = false;
	function isSpyEnabled() {
	    return spyEnabled;
	}
	function spyReport(event) {
	    if (!spyEnabled)
	        return false;
	    var listeners = globalState.spyListeners;
	    for (var i = 0, l = listeners.length; i < l; i++)
	        listeners[i](event);
	}
	function spyReportStart(event) {
	    var change = objectAssign({}, event, { spyReportStart: true });
	    spyReport(change);
	}
	var END_EVENT = { spyReportEnd: true };
	function spyReportEnd(change) {
	    if (change)
	        spyReport(objectAssign({}, change, END_EVENT));
	    else
	        spyReport(END_EVENT);
	}
	function spy(listener) {
	    globalState.spyListeners.push(listener);
	    spyEnabled = globalState.spyListeners.length > 0;
	    return once(function () {
	        var idx = globalState.spyListeners.indexOf(listener);
	        if (idx !== -1)
	            globalState.spyListeners.splice(idx, 1);
	        spyEnabled = globalState.spyListeners.length > 0;
	    });
	}
	exports.spy = spy;
	function trackTransitions(onReport) {
	    deprecated("trackTransitions is deprecated. Use mobx.spy instead");
	    if (typeof onReport === "boolean") {
	        deprecated("trackTransitions only takes a single callback function. If you are using the mobx-react-devtools, please update them first");
	        onReport = arguments[1];
	    }
	    if (!onReport) {
	        deprecated("trackTransitions without callback has been deprecated and is a no-op now. If you are using the mobx-react-devtools, please update them first");
	        return function () { };
	    }
	    return spy(onReport);
	}
	function transaction(action, thisArg, report) {
	    if (thisArg === void 0) { thisArg = undefined; }
	    if (report === void 0) { report = true; }
	    transactionStart((action.name) || "anonymous transaction", thisArg, report);
	    var res = action.call(thisArg);
	    transactionEnd(report);
	    return res;
	}
	exports.transaction = transaction;
	function transactionStart(name, thisArg, report) {
	    if (thisArg === void 0) { thisArg = undefined; }
	    if (report === void 0) { report = true; }
	    globalState.inTransaction += 1;
	    if (report && isSpyEnabled()) {
	        spyReportStart({
	            type: "transaction",
	            target: thisArg,
	            name: name
	        });
	    }
	}
	function transactionEnd(report) {
	    if (report === void 0) { report = true; }
	    if (--globalState.inTransaction === 0) {
	        var values = globalState.changedAtoms.splice(0);
	        for (var i = 0, l = values.length; i < l; i++)
	            propagateAtomReady(values[i]);
	        runReactions();
	    }
	    if (report && isSpyEnabled())
	        spyReportEnd();
	}
	function hasInterceptors(interceptable) {
	    return (interceptable.interceptors && interceptable.interceptors.length > 0);
	}
	function registerInterceptor(interceptable, handler) {
	    var interceptors = interceptable.interceptors || (interceptable.interceptors = []);
	    interceptors.push(handler);
	    return once(function () {
	        var idx = interceptors.indexOf(handler);
	        if (idx !== -1)
	            interceptors.splice(idx, 1);
	    });
	}
	function interceptChange(interceptable, change) {
	    var prevU = untrackedStart();
	    var interceptors = interceptable.interceptors;
	    for (var i = 0, l = interceptors.length; i < l; i++) {
	        change = interceptors[i](change);
	        invariant(!change || change.type, "Intercept handlers should return nothing or a change object");
	        if (!change)
	            return null;
	    }
	    untrackedEnd(prevU);
	    return change;
	}
	function hasListeners(listenable) {
	    return listenable.changeListeners && listenable.changeListeners.length > 0;
	}
	function registerListener(listenable, handler) {
	    var listeners = listenable.changeListeners || (listenable.changeListeners = []);
	    listeners.push(handler);
	    return once(function () {
	        var idx = listeners.indexOf(handler);
	        if (idx !== -1)
	            listeners.splice(idx, 1);
	    });
	}
	function notifyListeners(listenable, change) {
	    var prevU = untrackedStart();
	    var listeners = listenable.changeListeners;
	    if (!listeners)
	        return;
	    listeners = listeners.slice();
	    if (Array.isArray(change)) {
	        for (var i = 0, l = listeners.length; i < l; i++)
	            listeners[i].apply(null, change);
	    }
	    else {
	        for (var i = 0, l = listeners.length; i < l; i++)
	            listeners[i](change);
	    }
	    untrackedEnd(prevU);
	}
	var ValueMode;
	(function (ValueMode) {
	    ValueMode[ValueMode["Recursive"] = 0] = "Recursive";
	    ValueMode[ValueMode["Reference"] = 1] = "Reference";
	    ValueMode[ValueMode["Structure"] = 2] = "Structure";
	    ValueMode[ValueMode["Flat"] = 3] = "Flat";
	})(ValueMode || (ValueMode = {}));
	function asReference(value) {
	    return new AsReference(value);
	}
	exports.asReference = asReference;
	function asStructure(value) {
	    return new AsStructure(value);
	}
	exports.asStructure = asStructure;
	function asFlat(value) {
	    return new AsFlat(value);
	}
	exports.asFlat = asFlat;
	var AsReference = (function () {
	    function AsReference(value) {
	        this.value = value;
	        assertUnwrapped(value, "Modifiers are not allowed to be nested");
	    }
	    return AsReference;
	}());
	var AsStructure = (function () {
	    function AsStructure(value) {
	        this.value = value;
	        assertUnwrapped(value, "Modifiers are not allowed to be nested");
	    }
	    return AsStructure;
	}());
	var AsFlat = (function () {
	    function AsFlat(value) {
	        this.value = value;
	        assertUnwrapped(value, "Modifiers are not allowed to be nested");
	    }
	    return AsFlat;
	}());
	function asMap(data, modifierFunc) {
	    return map(data, modifierFunc);
	}
	exports.asMap = asMap;
	function getValueModeFromValue(value, defaultMode) {
	    if (value instanceof AsReference)
	        return [ValueMode.Reference, value.value];
	    if (value instanceof AsStructure)
	        return [ValueMode.Structure, value.value];
	    if (value instanceof AsFlat)
	        return [ValueMode.Flat, value.value];
	    return [defaultMode, value];
	}
	function getValueModeFromModifierFunc(func) {
	    if (func === asReference)
	        return ValueMode.Reference;
	    else if (func === asStructure)
	        return ValueMode.Structure;
	    else if (func === asFlat)
	        return ValueMode.Flat;
	    invariant(func === undefined, "Cannot determine value mode from function. Please pass in one of these: mobx.asReference, mobx.asStructure or mobx.asFlat, got: " + func);
	    return ValueMode.Recursive;
	}
	function makeChildObservable(value, parentMode, name) {
	    var childMode;
	    if (isObservable(value))
	        return value;
	    switch (parentMode) {
	        case ValueMode.Reference:
	            return value;
	        case ValueMode.Flat:
	            assertUnwrapped(value, "Items inside 'asFlat' cannot have modifiers");
	            childMode = ValueMode.Reference;
	            break;
	        case ValueMode.Structure:
	            assertUnwrapped(value, "Items inside 'asStructure' cannot have modifiers");
	            childMode = ValueMode.Structure;
	            break;
	        case ValueMode.Recursive:
	            _a = getValueModeFromValue(value, ValueMode.Recursive), childMode = _a[0], value = _a[1];
	            break;
	        default:
	            invariant(false, "Illegal State");
	    }
	    if (Array.isArray(value))
	        return createObservableArray(value, childMode, name);
	    if (isPlainObject(value) && Object.isExtensible(value))
	        return extendObservableHelper(value, value, childMode, name);
	    return value;
	    var _a;
	}
	function assertUnwrapped(value, message) {
	    if (value instanceof AsReference || value instanceof AsStructure || value instanceof AsFlat)
	        throw new Error("[mobx] asStructure / asReference / asFlat cannot be used here. " + message);
	}
	var OBSERVABLE_ARRAY_BUFFER_SIZE = 0;
	var StubArray = (function () {
	    function StubArray() {
	    }
	    return StubArray;
	}());
	StubArray.prototype = [];
	var ObservableArrayAdministration = (function () {
	    function ObservableArrayAdministration(name, mode, array, owned) {
	        this.mode = mode;
	        this.array = array;
	        this.owned = owned;
	        this.lastKnownLength = 0;
	        this.interceptors = null;
	        this.changeListeners = null;
	        this.atom = new Atom(name || ("ObservableArray@" + getNextId()));
	    }
	    ObservableArrayAdministration.prototype.makeReactiveArrayItem = function (value) {
	        assertUnwrapped(value, "Array values cannot have modifiers");
	        if (this.mode === ValueMode.Flat || this.mode === ValueMode.Reference)
	            return value;
	        return makeChildObservable(value, this.mode, this.atom.name + "[..]");
	    };
	    ObservableArrayAdministration.prototype.intercept = function (handler) {
	        return registerInterceptor(this, handler);
	    };
	    ObservableArrayAdministration.prototype.observe = function (listener, fireImmediately) {
	        if (fireImmediately === void 0) { fireImmediately = false; }
	        if (fireImmediately) {
	            listener({
	                object: this.array,
	                type: "splice",
	                index: 0,
	                added: this.values.slice(),
	                addedCount: this.values.length,
	                removed: [],
	                removedCount: 0
	            });
	        }
	        return registerListener(this, listener);
	    };
	    ObservableArrayAdministration.prototype.getArrayLength = function () {
	        this.atom.reportObserved();
	        return this.values.length;
	    };
	    ObservableArrayAdministration.prototype.setArrayLength = function (newLength) {
	        if (typeof newLength !== "number" || newLength < 0)
	            throw new Error("[mobx.array] Out of range: " + newLength);
	        var currentLength = this.values.length;
	        if (newLength === currentLength)
	            return;
	        else if (newLength > currentLength)
	            this.spliceWithArray(currentLength, 0, new Array(newLength - currentLength));
	        else
	            this.spliceWithArray(newLength, currentLength - newLength);
	    };
	    ObservableArrayAdministration.prototype.updateArrayLength = function (oldLength, delta) {
	        if (oldLength !== this.lastKnownLength)
	            throw new Error("[mobx] Modification exception: the internal structure of an observable array was changed. Did you use peek() to change it?");
	        this.lastKnownLength += delta;
	        if (delta > 0 && oldLength + delta > OBSERVABLE_ARRAY_BUFFER_SIZE)
	            reserveArrayBuffer(oldLength + delta);
	    };
	    ObservableArrayAdministration.prototype.spliceWithArray = function (index, deleteCount, newItems) {
	        checkIfStateModificationsAreAllowed();
	        var length = this.values.length;
	        if (index === undefined)
	            index = 0;
	        else if (index > length)
	            index = length;
	        else if (index < 0)
	            index = Math.max(0, length + index);
	        if (arguments.length === 1)
	            deleteCount = length - index;
	        else if (deleteCount === undefined || deleteCount === null)
	            deleteCount = 0;
	        else
	            deleteCount = Math.max(0, Math.min(deleteCount, length - index));
	        if (newItems === undefined)
	            newItems = [];
	        if (hasInterceptors(this)) {
	            var change = interceptChange(this, {
	                object: this.array,
	                type: "splice",
	                index: index,
	                removedCount: deleteCount,
	                added: newItems
	            });
	            if (!change)
	                return EMPTY_ARRAY;
	            deleteCount = change.removedCount;
	            newItems = change.added;
	        }
	        newItems = newItems.map(this.makeReactiveArrayItem, this);
	        var lengthDelta = newItems.length - deleteCount;
	        this.updateArrayLength(length, lengthDelta);
	        var res = (_a = this.values).splice.apply(_a, [index, deleteCount].concat(newItems));
	        if (deleteCount !== 0 || newItems.length !== 0)
	            this.notifyArraySplice(index, newItems, res);
	        return res;
	        var _a;
	    };
	    ObservableArrayAdministration.prototype.notifyArrayChildUpdate = function (index, newValue, oldValue) {
	        var notifySpy = !this.owned && isSpyEnabled();
	        var notify = hasListeners(this);
	        var change = notify || notifySpy ? {
	            object: this.array,
	            type: "update",
	            index: index, newValue: newValue, oldValue: oldValue
	        } : null;
	        if (notifySpy)
	            spyReportStart(change);
	        this.atom.reportChanged();
	        if (notify)
	            notifyListeners(this, change);
	        if (notifySpy)
	            spyReportEnd();
	    };
	    ObservableArrayAdministration.prototype.notifyArraySplice = function (index, added, removed) {
	        var notifySpy = !this.owned && isSpyEnabled();
	        var notify = hasListeners(this);
	        var change = notify || notifySpy ? {
	            object: this.array,
	            type: "splice",
	            index: index, removed: removed, added: added,
	            removedCount: removed.length,
	            addedCount: added.length
	        } : null;
	        if (notifySpy)
	            spyReportStart(change);
	        this.atom.reportChanged();
	        if (notify)
	            notifyListeners(this, change);
	        if (notifySpy)
	            spyReportEnd();
	    };
	    return ObservableArrayAdministration;
	}());
	var ObservableArray = (function (_super) {
	    __extends(ObservableArray, _super);
	    function ObservableArray(initialValues, mode, name, owned) {
	        if (owned === void 0) { owned = false; }
	        _super.call(this);
	        var adm = new ObservableArrayAdministration(name, mode, this, owned);
	        Object.defineProperty(this, "$mobx", {
	            enumerable: false,
	            configurable: false,
	            writable: false,
	            value: adm
	        });
	        if (initialValues && initialValues.length) {
	            adm.updateArrayLength(0, initialValues.length);
	            adm.values = initialValues.map(adm.makeReactiveArrayItem, adm);
	            adm.notifyArraySplice(0, adm.values.slice(), EMPTY_ARRAY);
	        }
	        else {
	            adm.values = [];
	        }
	    }
	    ObservableArray.prototype.intercept = function (handler) {
	        return this.$mobx.intercept(handler);
	    };
	    ObservableArray.prototype.observe = function (listener, fireImmediately) {
	        if (fireImmediately === void 0) { fireImmediately = false; }
	        return this.$mobx.observe(listener, fireImmediately);
	    };
	    ObservableArray.prototype.clear = function () {
	        return this.splice(0);
	    };
	    ObservableArray.prototype.replace = function (newItems) {
	        return this.$mobx.spliceWithArray(0, this.$mobx.values.length, newItems);
	    };
	    ObservableArray.prototype.toJS = function () {
	        return this.slice();
	    };
	    ObservableArray.prototype.toJSON = function () {
	        return this.toJS();
	    };
	    ObservableArray.prototype.peek = function () {
	        return this.$mobx.values;
	    };
	    ObservableArray.prototype.find = function (predicate, thisArg, fromIndex) {
	        if (fromIndex === void 0) { fromIndex = 0; }
	        this.$mobx.atom.reportObserved();
	        var items = this.$mobx.values, l = items.length;
	        for (var i = fromIndex; i < l; i++)
	            if (predicate.call(thisArg, items[i], i, this))
	                return items[i];
	        return null;
	    };
	    ObservableArray.prototype.splice = function (index, deleteCount) {
	        var newItems = [];
	        for (var _i = 2; _i < arguments.length; _i++) {
	            newItems[_i - 2] = arguments[_i];
	        }
	        switch (arguments.length) {
	            case 0:
	                return [];
	            case 1:
	                return this.$mobx.spliceWithArray(index);
	            case 2:
	                return this.$mobx.spliceWithArray(index, deleteCount);
	        }
	        return this.$mobx.spliceWithArray(index, deleteCount, newItems);
	    };
	    ObservableArray.prototype.push = function () {
	        var items = [];
	        for (var _i = 0; _i < arguments.length; _i++) {
	            items[_i - 0] = arguments[_i];
	        }
	        var adm = this.$mobx;
	        adm.spliceWithArray(adm.values.length, 0, items);
	        return adm.values.length;
	    };
	    ObservableArray.prototype.pop = function () {
	        return this.splice(Math.max(this.$mobx.values.length - 1, 0), 1)[0];
	    };
	    ObservableArray.prototype.shift = function () {
	        return this.splice(0, 1)[0];
	    };
	    ObservableArray.prototype.unshift = function () {
	        var items = [];
	        for (var _i = 0; _i < arguments.length; _i++) {
	            items[_i - 0] = arguments[_i];
	        }
	        var adm = this.$mobx;
	        adm.spliceWithArray(0, 0, items);
	        return adm.values.length;
	    };
	    ObservableArray.prototype.reverse = function () {
	        this.$mobx.atom.reportObserved();
	        var clone = this.slice();
	        return clone.reverse.apply(clone, arguments);
	    };
	    ObservableArray.prototype.sort = function (compareFn) {
	        this.$mobx.atom.reportObserved();
	        var clone = this.slice();
	        return clone.sort.apply(clone, arguments);
	    };
	    ObservableArray.prototype.remove = function (value) {
	        var idx = this.$mobx.values.indexOf(value);
	        if (idx > -1) {
	            this.splice(idx, 1);
	            return true;
	        }
	        return false;
	    };
	    ObservableArray.prototype.toString = function () {
	        return "[mobx.array] " + Array.prototype.toString.apply(this.$mobx.values, arguments);
	    };
	    ObservableArray.prototype.toLocaleString = function () {
	        return "[mobx.array] " + Array.prototype.toLocaleString.apply(this.$mobx.values, arguments);
	    };
	    return ObservableArray;
	}(StubArray));
	makeNonEnumerable(ObservableArray.prototype, [
	    "constructor",
	    "observe",
	    "clear",
	    "replace",
	    "toJSON",
	    "peek",
	    "find",
	    "splice",
	    "push",
	    "pop",
	    "shift",
	    "unshift",
	    "reverse",
	    "sort",
	    "remove",
	    "toString",
	    "toLocaleString"
	]);
	Object.defineProperty(ObservableArray.prototype, "length", {
	    enumerable: false,
	    configurable: true,
	    get: function () {
	        return this.$mobx.getArrayLength();
	    },
	    set: function (newLength) {
	        this.$mobx.setArrayLength(newLength);
	    }
	});
	[
	    "concat",
	    "every",
	    "filter",
	    "forEach",
	    "indexOf",
	    "join",
	    "lastIndexOf",
	    "map",
	    "reduce",
	    "reduceRight",
	    "slice",
	    "some"
	].forEach(function (funcName) {
	    var baseFunc = Array.prototype[funcName];
	    Object.defineProperty(ObservableArray.prototype, funcName, {
	        configurable: false,
	        writable: true,
	        enumerable: false,
	        value: function () {
	            this.$mobx.atom.reportObserved();
	            return baseFunc.apply(this.$mobx.values, arguments);
	        }
	    });
	});
	function createArrayBufferItem(index) {
	    Object.defineProperty(ObservableArray.prototype, "" + index, {
	        enumerable: false,
	        configurable: false,
	        set: createArraySetter(index),
	        get: createArrayGetter(index)
	    });
	}
	function createArraySetter(index) {
	    return function (newValue) {
	        var adm = this.$mobx;
	        var values = adm.values;
	        assertUnwrapped(newValue, "Modifiers cannot be used on array values. For non-reactive array values use makeReactive(asFlat(array)).");
	        if (index < values.length) {
	            checkIfStateModificationsAreAllowed();
	            var oldValue = values[index];
	            if (hasInterceptors(adm)) {
	                var change = interceptChange(adm, {
	                    type: "update",
	                    object: adm.array,
	                    index: index, newValue: newValue
	                });
	                if (!change)
	                    return;
	                newValue = change.newValue;
	            }
	            newValue = adm.makeReactiveArrayItem(newValue);
	            var changed = (adm.mode === ValueMode.Structure) ? !deepEquals(oldValue, newValue) : oldValue !== newValue;
	            if (changed) {
	                values[index] = newValue;
	                adm.notifyArrayChildUpdate(index, newValue, oldValue);
	            }
	        }
	        else if (index === values.length) {
	            adm.spliceWithArray(index, 0, [newValue]);
	        }
	        else
	            throw new Error("[mobx.array] Index out of bounds, " + index + " is larger than " + values.length);
	    };
	}
	function createArrayGetter(index) {
	    return function () {
	        var impl = this.$mobx;
	        if (impl && index < impl.values.length) {
	            impl.atom.reportObserved();
	            return impl.values[index];
	        }
	        return undefined;
	    };
	}
	function reserveArrayBuffer(max) {
	    for (var index = OBSERVABLE_ARRAY_BUFFER_SIZE; index < max; index++)
	        createArrayBufferItem(index);
	    OBSERVABLE_ARRAY_BUFFER_SIZE = max;
	}
	reserveArrayBuffer(1000);
	function createObservableArray(initialValues, mode, name) {
	    return new ObservableArray(initialValues, mode, name);
	}
	function fastArray(initialValues) {
	    deprecated("fastArray is deprecated. Please use `observable(asFlat([]))`");
	    return createObservableArray(initialValues, ValueMode.Flat, null);
	}
	exports.fastArray = fastArray;
	function isObservableArray(thing) {
	    return thing instanceof ObservableArray;
	}
	exports.isObservableArray = isObservableArray;
	var ObservableMapMarker = {};
	var ObservableMap = (function () {
	    function ObservableMap(initialData, valueModeFunc) {
	        var _this = this;
	        this.$mobx = ObservableMapMarker;
	        this._data = {};
	        this._hasMap = {};
	        this.name = "ObservableMap@" + getNextId();
	        this._keys = new ObservableArray(null, ValueMode.Reference, this.name + ".keys()", true);
	        this.interceptors = null;
	        this.changeListeners = null;
	        this._valueMode = getValueModeFromModifierFunc(valueModeFunc);
	        if (this._valueMode === ValueMode.Flat)
	            this._valueMode = ValueMode.Reference;
	        allowStateChanges(true, function () {
	            if (isPlainObject(initialData))
	                _this.merge(initialData);
	            else if (Array.isArray(initialData))
	                initialData.forEach(function (_a) {
	                    var key = _a[0], value = _a[1];
	                    return _this.set(key, value);
	                });
	        });
	    }
	    ObservableMap.prototype._has = function (key) {
	        return typeof this._data[key] !== "undefined";
	    };
	    ObservableMap.prototype.has = function (key) {
	        if (!this.isValidKey(key))
	            return false;
	        key = "" + key;
	        if (this._hasMap[key])
	            return this._hasMap[key].get();
	        return this._updateHasMapEntry(key, false).get();
	    };
	    ObservableMap.prototype.set = function (key, value) {
	        this.assertValidKey(key);
	        key = "" + key;
	        var hasKey = this._has(key);
	        assertUnwrapped(value, "[mobx.map.set] Expected unwrapped value to be inserted to key '" + key + "'. If you need to use modifiers pass them as second argument to the constructor");
	        if (hasInterceptors(this)) {
	            var change = interceptChange(this, {
	                type: hasKey ? "update" : "add",
	                object: this,
	                newValue: value,
	                name: key
	            });
	            if (!change)
	                return;
	            value = change.newValue;
	        }
	        if (hasKey) {
	            this._updateValue(key, value);
	        }
	        else {
	            this._addValue(key, value);
	        }
	    };
	    ObservableMap.prototype.delete = function (key) {
	        var _this = this;
	        this.assertValidKey(key);
	        key = "" + key;
	        if (hasInterceptors(this)) {
	            var change = interceptChange(this, {
	                type: "delete",
	                object: this,
	                name: key
	            });
	            if (!change)
	                return;
	        }
	        if (this._has(key)) {
	            var notifySpy = isSpyEnabled();
	            var notify = hasListeners(this);
	            var change = notify || notifySpy ? {
	                type: "delete",
	                object: this,
	                oldValue: this._data[key].value,
	                name: key
	            } : null;
	            if (notifySpy)
	                spyReportStart(change);
	            transaction(function () {
	                _this._keys.remove(key);
	                _this._updateHasMapEntry(key, false);
	                var observable = _this._data[key];
	                observable.setNewValue(undefined);
	                _this._data[key] = undefined;
	            }, undefined, false);
	            if (notify)
	                notifyListeners(this, change);
	            if (notifySpy)
	                spyReportEnd();
	        }
	    };
	    ObservableMap.prototype._updateHasMapEntry = function (key, value) {
	        var entry = this._hasMap[key];
	        if (entry) {
	            entry.setNewValue(value);
	        }
	        else {
	            entry = this._hasMap[key] = new ObservableValue(value, ValueMode.Reference, this.name + "." + key + "?", false);
	        }
	        return entry;
	    };
	    ObservableMap.prototype._updateValue = function (name, newValue) {
	        var observable = this._data[name];
	        newValue = observable.prepareNewValue(newValue);
	        if (newValue !== UNCHANGED) {
	            var notifySpy = isSpyEnabled();
	            var notify = hasListeners(this);
	            var change = notify || notifySpy ? {
	                type: "update",
	                object: this,
	                oldValue: observable.value,
	                name: name, newValue: newValue
	            } : null;
	            if (notifySpy)
	                spyReportStart(change);
	            observable.setNewValue(newValue);
	            if (notify)
	                notifyListeners(this, change);
	            if (notifySpy)
	                spyReportEnd();
	        }
	    };
	    ObservableMap.prototype._addValue = function (name, newValue) {
	        var _this = this;
	        transaction(function () {
	            var observable = _this._data[name] = new ObservableValue(newValue, _this._valueMode, _this.name + "." + name, false);
	            newValue = observable.value;
	            _this._updateHasMapEntry(name, true);
	            _this._keys.push(name);
	        }, undefined, false);
	        var notifySpy = isSpyEnabled();
	        var notify = hasListeners(this);
	        var change = notify || notifySpy ? {
	            type: "add",
	            object: this,
	            name: name, newValue: newValue
	        } : null;
	        if (notifySpy)
	            spyReportStart(change);
	        if (notify)
	            notifyListeners(this, change);
	        if (notifySpy)
	            spyReportEnd();
	    };
	    ObservableMap.prototype.get = function (key) {
	        key = "" + key;
	        if (this.has(key))
	            return this._data[key].get();
	        return undefined;
	    };
	    ObservableMap.prototype.keys = function () {
	        return this._keys.slice();
	    };
	    ObservableMap.prototype.values = function () {
	        return this.keys().map(this.get, this);
	    };
	    ObservableMap.prototype.entries = function () {
	        var _this = this;
	        return this.keys().map(function (key) { return [key, _this.get(key)]; });
	    };
	    ObservableMap.prototype.forEach = function (callback, thisArg) {
	        var _this = this;
	        this.keys().forEach(function (key) { return callback.call(thisArg, _this.get(key), key); });
	    };
	    ObservableMap.prototype.merge = function (other) {
	        var _this = this;
	        transaction(function () {
	            if (other instanceof ObservableMap)
	                other.keys().forEach(function (key) { return _this.set(key, other.get(key)); });
	            else
	                Object.keys(other).forEach(function (key) { return _this.set(key, other[key]); });
	        }, undefined, false);
	        return this;
	    };
	    ObservableMap.prototype.clear = function () {
	        var _this = this;
	        transaction(function () {
	            untracked(function () {
	                _this.keys().forEach(_this.delete, _this);
	            });
	        }, undefined, false);
	    };
	    Object.defineProperty(ObservableMap.prototype, "size", {
	        get: function () {
	            return this._keys.length;
	        },
	        enumerable: true,
	        configurable: true
	    });
	    ObservableMap.prototype.toJS = function () {
	        var _this = this;
	        var res = {};
	        this.keys().forEach(function (key) { return res[key] = _this.get(key); });
	        return res;
	    };
	    ObservableMap.prototype.toJs = function () {
	        deprecated("toJs is deprecated, use toJS instead");
	        return this.toJS();
	    };
	    ObservableMap.prototype.toJSON = function () {
	        return this.toJS();
	    };
	    ObservableMap.prototype.isValidKey = function (key) {
	        if (key === null || key === undefined)
	            return false;
	        if (typeof key !== "string" && typeof key !== "number" && typeof key !== "boolean")
	            return false;
	        return true;
	    };
	    ObservableMap.prototype.assertValidKey = function (key) {
	        if (!this.isValidKey(key))
	            throw new Error("[mobx.map] Invalid key: '" + key + "'");
	    };
	    ObservableMap.prototype.toString = function () {
	        var _this = this;
	        return this.name + "[{ " + this.keys().map(function (key) { return (key + ": " + ("" + _this.get(key))); }).join(", ") + " }]";
	    };
	    ObservableMap.prototype.observe = function (listener, fireImmediately) {
	        invariant(fireImmediately !== true, "`observe` doesn't support the fire immediately property for observable maps.");
	        return registerListener(this, listener);
	    };
	    ObservableMap.prototype.intercept = function (handler) {
	        return registerInterceptor(this, handler);
	    };
	    return ObservableMap;
	}());
	exports.ObservableMap = ObservableMap;
	function map(initialValues, valueModifier) {
	    return new ObservableMap(initialValues, valueModifier);
	}
	exports.map = map;
	function isObservableMap(thing) {
	    return thing instanceof ObservableMap;
	}
	exports.isObservableMap = isObservableMap;
	var ObservableObjectAdministration = (function () {
	    function ObservableObjectAdministration(target, name, mode) {
	        this.target = target;
	        this.name = name;
	        this.mode = mode;
	        this.values = {};
	        this.changeListeners = null;
	        this.interceptors = null;
	    }
	    ObservableObjectAdministration.prototype.observe = function (callback, fireImmediately) {
	        invariant(fireImmediately !== true, "`observe` doesn't support the fire immediately property for observable objects.");
	        return registerListener(this, callback);
	    };
	    ObservableObjectAdministration.prototype.intercept = function (handler) {
	        return registerInterceptor(this, handler);
	    };
	    return ObservableObjectAdministration;
	}());
	function asObservableObject(target, name, mode) {
	    if (mode === void 0) { mode = ValueMode.Recursive; }
	    if (isObservableObject(target))
	        return target.$mobx;
	    if (!isPlainObject(target))
	        name = target.constructor.name + "@" + getNextId();
	    if (!name)
	        name = "ObservableObject@" + getNextId();
	    var adm = new ObservableObjectAdministration(target, name, mode);
	    Object.defineProperty(target, "$mobx", {
	        enumerable: false,
	        configurable: false,
	        writable: false,
	        value: adm
	    });
	    return adm;
	}
	function setObservableObjectInstanceProperty(adm, propName, value) {
	    if (adm.values[propName])
	        adm.target[propName] = value;
	    else
	        defineObservableProperty(adm, propName, value, true);
	}
	function defineObservableProperty(adm, propName, newValue, asInstanceProperty) {
	    if (asInstanceProperty)
	        assertPropertyConfigurable(adm.target, propName);
	    var observable;
	    var name = adm.name + "." + propName;
	    var isComputed = true;
	    if (typeof newValue === "function" && newValue.length === 0 && !isAction(newValue))
	        observable = new ComputedValue(newValue, adm.target, false, name);
	    else if (newValue instanceof AsStructure && typeof newValue.value === "function" && newValue.value.length === 0)
	        observable = new ComputedValue(newValue.value, adm.target, true, name);
	    else {
	        isComputed = false;
	        if (hasInterceptors(adm)) {
	            var change = interceptChange(adm, {
	                object: adm.target,
	                name: propName,
	                type: "add",
	                newValue: newValue
	            });
	            if (!change)
	                return;
	            newValue = change.newValue;
	        }
	        observable = new ObservableValue(newValue, adm.mode, name, false);
	        newValue = observable.value;
	    }
	    adm.values[propName] = observable;
	    if (asInstanceProperty) {
	        Object.defineProperty(adm.target, propName, {
	            configurable: true,
	            enumerable: !isComputed,
	            get: function () {
	                return observable.get();
	            },
	            set: isComputed
	                ? throwingComputedValueSetter
	                : function (v) {
	                    setPropertyValue(this, propName, v);
	                }
	        });
	    }
	    if (!isComputed)
	        notifyPropertyAddition(adm, adm.target, propName, newValue);
	}
	function setPropertyValue(instance, name, newValue) {
	    var adm = instance.$mobx;
	    var observable = instance.$mobx.values[name];
	    if (hasInterceptors(adm)) {
	        var change = interceptChange(adm, {
	            type: "update",
	            object: instance,
	            name: name, newValue: newValue
	        });
	        if (!change)
	            return;
	        newValue = change.newValue;
	    }
	    newValue = observable.prepareNewValue(newValue);
	    if (newValue !== UNCHANGED) {
	        var notify = hasListeners(adm);
	        var notifySpy = isSpyEnabled();
	        var change = notifyListeners || hasListeners ? {
	            type: "update",
	            object: instance,
	            oldValue: observable.value,
	            name: name, newValue: newValue
	        } : null;
	        if (notifySpy)
	            spyReportStart(change);
	        observable.setNewValue(newValue);
	        if (notify)
	            notifyListeners(adm, change);
	        if (notifySpy)
	            spyReportEnd();
	    }
	}
	function notifyPropertyAddition(adm, object, name, newValue) {
	    var notify = hasListeners(adm);
	    var notifySpy = isSpyEnabled();
	    var change = notify || notifySpy ? {
	        type: "add",
	        object: object, name: name, newValue: newValue
	    } : null;
	    if (notifySpy)
	        spyReportStart(change);
	    if (notify)
	        notifyListeners(adm, change);
	    if (notifySpy)
	        spyReportEnd();
	}
	function isObservableObject(thing) {
	    if (typeof thing === "object" && thing !== null) {
	        runLazyInitializers(thing);
	        return thing.$mobx instanceof ObservableObjectAdministration;
	    }
	    return false;
	}
	exports.isObservableObject = isObservableObject;
	var UNCHANGED = {};
	var ObservableValue = (function (_super) {
	    __extends(ObservableValue, _super);
	    function ObservableValue(value, mode, name, notifySpy) {
	        if (name === void 0) { name = "ObservableValue@" + getNextId(); }
	        if (notifySpy === void 0) { notifySpy = true; }
	        _super.call(this, name);
	        this.mode = mode;
	        this.hasUnreportedChange = false;
	        this.value = undefined;
	        var _a = getValueModeFromValue(value, ValueMode.Recursive), childmode = _a[0], unwrappedValue = _a[1];
	        if (this.mode === ValueMode.Recursive)
	            this.mode = childmode;
	        this.value = makeChildObservable(unwrappedValue, this.mode, this.name);
	        if (notifySpy && isSpyEnabled()) {
	            spyReport({ type: "create", object: this, newValue: this.value });
	        }
	    }
	    ObservableValue.prototype.set = function (newValue) {
	        var oldValue = this.value;
	        newValue = this.prepareNewValue(newValue);
	        if (newValue !== UNCHANGED) {
	            var notifySpy = isSpyEnabled();
	            if (notifySpy) {
	                spyReportStart({
	                    type: "update",
	                    object: this,
	                    newValue: newValue, oldValue: oldValue
	                });
	            }
	            this.setNewValue(newValue);
	            if (notifySpy)
	                spyReportEnd();
	        }
	    };
	    ObservableValue.prototype.prepareNewValue = function (newValue) {
	        assertUnwrapped(newValue, "Modifiers cannot be used on non-initial values.");
	        checkIfStateModificationsAreAllowed();
	        if (hasInterceptors(this)) {
	            var change = interceptChange(this, { object: this, type: "update", newValue: newValue });
	            if (!change)
	                return UNCHANGED;
	            newValue = change.newValue;
	        }
	        var changed = valueDidChange(this.mode === ValueMode.Structure, this.value, newValue);
	        if (changed)
	            return makeChildObservable(newValue, this.mode, this.name);
	        return UNCHANGED;
	    };
	    ObservableValue.prototype.setNewValue = function (newValue) {
	        var oldValue = this.value;
	        this.value = newValue;
	        this.reportChanged();
	        if (hasListeners(this))
	            notifyListeners(this, [newValue, oldValue]);
	    };
	    ObservableValue.prototype.get = function () {
	        this.reportObserved();
	        return this.value;
	    };
	    ObservableValue.prototype.intercept = function (handler) {
	        return registerInterceptor(this, handler);
	    };
	    ObservableValue.prototype.observe = function (listener, fireImmediately) {
	        if (fireImmediately)
	            listener(this.value, undefined);
	        return registerListener(this, listener);
	    };
	    ObservableValue.prototype.toJSON = function () {
	        return this.get();
	    };
	    ObservableValue.prototype.toString = function () {
	        return this.name + "[" + this.value + "]";
	    };
	    return ObservableValue;
	}(Atom));
	function getAtom(thing, property) {
	    if (typeof thing === "object" && thing !== null) {
	        if (isObservableArray(thing)) {
	            invariant(property === undefined, "It is not possible to get index atoms from arrays");
	            return thing.$mobx.atom;
	        }
	        if (isObservableMap(thing)) {
	            if (property === undefined)
	                return getAtom(thing._keys);
	            var observable_1 = thing._data[property] || thing._hasMap[property];
	            invariant(!!observable_1, "the entry '" + property + "' does not exist in the observable map '" + getDebugName(thing) + "'");
	            return observable_1;
	        }
	        runLazyInitializers(thing);
	        if (isObservableObject(thing)) {
	            invariant(!!property, "please specify a property");
	            var observable_2 = thing.$mobx.values[property];
	            invariant(!!observable_2, "no observable property '" + property + "' found on the observable object '" + getDebugName(thing) + "'");
	            return observable_2;
	        }
	        if (thing instanceof Atom || thing instanceof ComputedValue || thing instanceof Reaction) {
	            return thing;
	        }
	    }
	    else if (typeof thing === "function") {
	        if (thing.$mobx instanceof Reaction) {
	            return thing.$mobx;
	        }
	    }
	    invariant(false, "Cannot obtain atom from " + thing);
	}
	function getAdministration(thing, property) {
	    invariant(thing, "Expection some object");
	    if (property !== undefined)
	        return getAdministration(getAtom(thing, property));
	    if (thing instanceof Atom || thing instanceof ComputedValue || thing instanceof Reaction)
	        return thing;
	    if (isObservableMap(thing))
	        return thing;
	    runLazyInitializers(thing);
	    if (thing.$mobx)
	        return thing.$mobx;
	    invariant(false, "Cannot obtain administration from " + thing);
	}
	function getDebugName(thing, property) {
	    var named;
	    if (property !== undefined)
	        named = getAtom(thing, property);
	    else if (isObservableObject(thing) || isObservableMap(thing))
	        named = getAdministration(thing);
	    else
	        named = getAtom(thing);
	    return named.name;
	}
	function createClassPropertyDecorator(onInitialize, get, set, enumerable, allowCustomArguments) {
	    function classPropertyDecorator(target, key, descriptor, customArgs) {
	        invariant(allowCustomArguments || quacksLikeADecorator(arguments), "This function is a decorator, but it wasn't invoked like a decorator");
	        if (!descriptor) {
	            return {
	                enumerable: enumerable,
	                configurable: true,
	                get: function () {
	                    if (!this.__mobxInitializedProps || this.__mobxInitializedProps[key] !== true)
	                        typescriptInitializeProperty(this, key, undefined, onInitialize, customArgs, descriptor);
	                    return get.call(this, key);
	                },
	                set: function (v) {
	                    if (!this.__mobxInitializedProps || this.__mobxInitializedProps[key] !== true) {
	                        typescriptInitializeProperty(this, key, v, onInitialize, customArgs, descriptor);
	                    }
	                    else {
	                        set.call(this, key, v);
	                    }
	                }
	            };
	        }
	        else {
	            if (!target.hasOwnProperty("__mobxLazyInitializers")) {
	                Object.defineProperty(target, "__mobxLazyInitializers", {
	                    writable: false, configurable: false, enumerable: false,
	                    value: (target.__mobxLazyInitializers && target.__mobxLazyInitializers.slice()) || []
	                });
	            }
	            var value_1 = descriptor.value, initializer_1 = descriptor.initializer;
	            target.__mobxLazyInitializers.push(function (instance) {
	                onInitialize(instance, key, (initializer_1 ? initializer_1.call(instance) : value_1), customArgs, descriptor);
	            });
	            return {
	                enumerable: enumerable, configurable: true,
	                get: function () {
	                    if (this.__mobxDidRunLazyInitializers !== true)
	                        runLazyInitializers(this);
	                    return get.call(this, key);
	                },
	                set: function (v) {
	                    if (this.__mobxDidRunLazyInitializers !== true)
	                        runLazyInitializers(this);
	                    set.call(this, key, v);
	                }
	            };
	        }
	    }
	    if (allowCustomArguments) {
	        return function () {
	            if (quacksLikeADecorator(arguments))
	                return classPropertyDecorator.apply(null, arguments);
	            var outerArgs = arguments;
	            return function (target, key, descriptor) { return classPropertyDecorator(target, key, descriptor, outerArgs); };
	        };
	    }
	    return classPropertyDecorator;
	}
	function typescriptInitializeProperty(instance, key, v, onInitialize, customArgs, baseDescriptor) {
	    if (!instance.hasOwnProperty("__mobxInitializedProps")) {
	        Object.defineProperty(instance, "__mobxInitializedProps", {
	            enumerable: false, configurable: false, writable: true,
	            value: {}
	        });
	    }
	    instance.__mobxInitializedProps[key] = true;
	    onInitialize(instance, key, v, customArgs, baseDescriptor);
	}
	function runLazyInitializers(instance) {
	    if (instance.__mobxDidRunLazyInitializers === true)
	        return;
	    if (instance.__mobxLazyInitializers) {
	        Object.defineProperty(instance, "__mobxDidRunLazyInitializers", {
	            enumerable: false,
	            configurable: false,
	            writable: false,
	            value: true
	        });
	        instance.__mobxDidRunLazyInitializers && instance.__mobxLazyInitializers.forEach(function (initializer) { return initializer(instance); });
	    }
	}
	function quacksLikeADecorator(args) {
	    return (args.length === 2 || args.length === 3) && typeof args[1] === "string";
	}
	var SimpleEventEmitter = (function () {
	    function SimpleEventEmitter() {
	        this.listeners = [];
	        deprecated("extras.SimpleEventEmitter is deprecated and will be removed in the next major release");
	    }
	    SimpleEventEmitter.prototype.emit = function () {
	        var listeners = this.listeners.slice();
	        for (var i = 0, l = listeners.length; i < l; i++)
	            listeners[i].apply(null, arguments);
	    };
	    SimpleEventEmitter.prototype.on = function (listener) {
	        var _this = this;
	        this.listeners.push(listener);
	        return once(function () {
	            var idx = _this.listeners.indexOf(listener);
	            if (idx !== -1)
	                _this.listeners.splice(idx, 1);
	        });
	    };
	    SimpleEventEmitter.prototype.once = function (listener) {
	        var subscription = this.on(function () {
	            subscription();
	            listener.apply(this, arguments);
	        });
	        return subscription;
	    };
	    return SimpleEventEmitter;
	}());
	exports.SimpleEventEmitter = SimpleEventEmitter;
	var EMPTY_ARRAY = [];
	Object.freeze(EMPTY_ARRAY);
	function getNextId() {
	    return ++globalState.mobxGuid;
	}
	function invariant(check, message, thing) {
	    if (!check)
	        throw new Error("[mobx] Invariant failed: " + message + (thing ? " in '" + thing + "'" : ""));
	}
	var deprecatedMessages = [];
	function deprecated(msg) {
	    if (deprecatedMessages.indexOf(msg) !== -1)
	        return;
	    deprecatedMessages.push(msg);
	    console.error("[mobx] Deprecated: " + msg);
	}
	function once(func) {
	    var invoked = false;
	    return function () {
	        if (invoked)
	            return;
	        invoked = true;
	        return func.apply(this, arguments);
	    };
	}
	var noop = function () { };
	function unique(list) {
	    var res = [];
	    list.forEach(function (item) {
	        if (res.indexOf(item) === -1)
	            res.push(item);
	    });
	    return res;
	}
	function joinStrings(things, limit, separator) {
	    if (limit === void 0) { limit = 100; }
	    if (separator === void 0) { separator = " - "; }
	    if (!things)
	        return "";
	    var sliced = things.slice(0, limit);
	    return "" + sliced.join(separator) + (things.length > limit ? " (... and " + (things.length - limit) + "more)" : "");
	}
	function isPlainObject(value) {
	    return value !== null && typeof value === "object" && Object.getPrototypeOf(value) === Object.prototype;
	}
	function objectAssign() {
	    var res = arguments[0];
	    for (var i = 1, l = arguments.length; i < l; i++) {
	        var source = arguments[i];
	        for (var key in source)
	            if (source.hasOwnProperty(key)) {
	                res[key] = source[key];
	            }
	    }
	    return res;
	}
	function valueDidChange(compareStructural, oldValue, newValue) {
	    return compareStructural
	        ? !deepEquals(oldValue, newValue)
	        : oldValue !== newValue;
	}
	function makeNonEnumerable(object, props) {
	    for (var i = 0; i < props.length; i++) {
	        Object.defineProperty(object, props[i], {
	            configurable: true,
	            writable: true,
	            enumerable: false,
	            value: object[props[i]]
	        });
	    }
	}
	function isPropertyConfigurable(object, prop) {
	    var descriptor = Object.getOwnPropertyDescriptor(object, prop);
	    return !descriptor || (descriptor.configurable !== false && descriptor.writable !== false);
	}
	function assertPropertyConfigurable(object, prop) {
	    invariant(isPropertyConfigurable(object, prop), "Cannot make property '" + prop + "' observable, it is not configurable and writable in the target object");
	}
	function getEnumerableKeys(obj) {
	    var res = [];
	    for (var key in obj)
	        res.push(key);
	    return res;
	}
	function deepEquals(a, b) {
	    if (a === null && b === null)
	        return true;
	    if (a === undefined && b === undefined)
	        return true;
	    var aIsArray = Array.isArray(a) || isObservableArray(a);
	    if (aIsArray !== (Array.isArray(b) || isObservableArray(b))) {
	        return false;
	    }
	    else if (aIsArray) {
	        if (a.length !== b.length)
	            return false;
	        for (var i = a.length; i >= 0; i--)
	            if (!deepEquals(a[i], b[i]))
	                return false;
	        return true;
	    }
	    else if (typeof a === "object" && typeof b === "object") {
	        if (a === null || b === null)
	            return false;
	        if (getEnumerableKeys(a).length !== getEnumerableKeys(b).length)
	            return false;
	        for (var prop in a) {
	            if (!(prop in b))
	                return false;
	            if (!deepEquals(a[prop], b[prop]))
	                return false;
	        }
	        return true;
	    }
	    return a === b;
	}
	function quickDiff(current, base) {
	    if (!base || !base.length)
	        return [current, []];
	    if (!current || !current.length)
	        return [[], base];
	    var added = [];
	    var removed = [];
	    var currentIndex = 0, currentSearch = 0, currentLength = current.length, currentExhausted = false, baseIndex = 0, baseSearch = 0, baseLength = base.length, isSearching = false, baseExhausted = false;
	    while (!baseExhausted && !currentExhausted) {
	        if (!isSearching) {
	            if (currentIndex < currentLength && baseIndex < baseLength && current[currentIndex] === base[baseIndex]) {
	                currentIndex++;
	                baseIndex++;
	                if (currentIndex === currentLength && baseIndex === baseLength)
	                    return [added, removed];
	                continue;
	            }
	            currentSearch = currentIndex;
	            baseSearch = baseIndex;
	            isSearching = true;
	        }
	        baseSearch += 1;
	        currentSearch += 1;
	        if (baseSearch >= baseLength)
	            baseExhausted = true;
	        if (currentSearch >= currentLength)
	            currentExhausted = true;
	        if (!currentExhausted && current[currentSearch] === base[baseIndex]) {
	            added = added.concat(current.slice(currentIndex, currentSearch));
	            currentIndex = currentSearch + 1;
	            baseIndex++;
	            isSearching = false;
	        }
	        else if (!baseExhausted && base[baseSearch] === current[currentIndex]) {
	            removed = removed.concat(base.slice(baseIndex, baseSearch));
	            baseIndex = baseSearch + 1;
	            currentIndex++;
	            isSearching = false;
	        }
	    }
	    return [
	        added.concat(current.slice(currentIndex)),
	        removed.concat(base.slice(baseIndex))
	    ];
	}
	var _a;
	
	/* WEBPACK VAR INJECTION */}.call(exports, (function() { return this; }())))

/***/ },

/***/ 174:
/*!**************************!*\
  !*** ./~/axios/index.js ***!
  \**************************/
/***/ function(module, exports, __webpack_require__) {

	module.exports = __webpack_require__(/*! ./lib/axios */ 175);

/***/ },

/***/ 175:
/*!******************************!*\
  !*** ./~/axios/lib/axios.js ***!
  \******************************/
/***/ function(module, exports, __webpack_require__) {

	'use strict';
	
	var defaults = __webpack_require__(/*! ./defaults */ 176);
	var utils = __webpack_require__(/*! ./utils */ 177);
	var dispatchRequest = __webpack_require__(/*! ./core/dispatchRequest */ 179);
	var InterceptorManager = __webpack_require__(/*! ./core/InterceptorManager */ 188);
	var isAbsoluteURL = __webpack_require__(/*! ./helpers/isAbsoluteURL */ 189);
	var combineURLs = __webpack_require__(/*! ./helpers/combineURLs */ 190);
	var bind = __webpack_require__(/*! ./helpers/bind */ 191);
	var transformData = __webpack_require__(/*! ./helpers/transformData */ 183);
	
	function Axios(defaultConfig) {
	  this.defaults = utils.merge({}, defaultConfig);
	  this.interceptors = {
	    request: new InterceptorManager(),
	    response: new InterceptorManager()
	  };
	}
	
	Axios.prototype.request = function request(config) {
	  /*eslint no-param-reassign:0*/
	  // Allow for axios('example/url'[, config]) a la fetch API
	  if (typeof config === 'string') {
	    config = utils.merge({
	      url: arguments[0]
	    }, arguments[1]);
	  }
	
	  config = utils.merge(defaults, this.defaults, { method: 'get' }, config);
	
	  // Support baseURL config
	  if (config.baseURL && !isAbsoluteURL(config.url)) {
	    config.url = combineURLs(config.baseURL, config.url);
	  }
	
	  // Don't allow overriding defaults.withCredentials
	  config.withCredentials = config.withCredentials || this.defaults.withCredentials;
	
	  // Transform request data
	  config.data = transformData(
	    config.data,
	    config.headers,
	    config.transformRequest
	  );
	
	  // Flatten headers
	  config.headers = utils.merge(
	    config.headers.common || {},
	    config.headers[config.method] || {},
	    config.headers || {}
	  );
	
	  utils.forEach(
	    ['delete', 'get', 'head', 'post', 'put', 'patch', 'common'],
	    function cleanHeaderConfig(method) {
	      delete config.headers[method];
	    }
	  );
	
	  // Hook up interceptors middleware
	  var chain = [dispatchRequest, undefined];
	  var promise = Promise.resolve(config);
	
	  this.interceptors.request.forEach(function unshiftRequestInterceptors(interceptor) {
	    chain.unshift(interceptor.fulfilled, interceptor.rejected);
	  });
	
	  this.interceptors.response.forEach(function pushResponseInterceptors(interceptor) {
	    chain.push(interceptor.fulfilled, interceptor.rejected);
	  });
	
	  while (chain.length) {
	    promise = promise.then(chain.shift(), chain.shift());
	  }
	
	  return promise;
	};
	
	var defaultInstance = new Axios(defaults);
	var axios = module.exports = bind(Axios.prototype.request, defaultInstance);
	axios.request = bind(Axios.prototype.request, defaultInstance);
	
	// Expose Axios class to allow class inheritance
	axios.Axios = Axios;
	
	// Expose properties from defaultInstance
	axios.defaults = defaultInstance.defaults;
	axios.interceptors = defaultInstance.interceptors;
	
	// Factory for creating new instances
	axios.create = function create(defaultConfig) {
	  return new Axios(defaultConfig);
	};
	
	// Expose all/spread
	axios.all = function all(promises) {
	  return Promise.all(promises);
	};
	axios.spread = __webpack_require__(/*! ./helpers/spread */ 192);
	
	// Provide aliases for supported request methods
	utils.forEach(['delete', 'get', 'head'], function forEachMethodNoData(method) {
	  /*eslint func-names:0*/
	  Axios.prototype[method] = function(url, config) {
	    return this.request(utils.merge(config || {}, {
	      method: method,
	      url: url
	    }));
	  };
	  axios[method] = bind(Axios.prototype[method], defaultInstance);
	});
	
	utils.forEach(['post', 'put', 'patch'], function forEachMethodWithData(method) {
	  /*eslint func-names:0*/
	  Axios.prototype[method] = function(url, data, config) {
	    return this.request(utils.merge(config || {}, {
	      method: method,
	      url: url,
	      data: data
	    }));
	  };
	  axios[method] = bind(Axios.prototype[method], defaultInstance);
	});


/***/ },

/***/ 176:
/*!*********************************!*\
  !*** ./~/axios/lib/defaults.js ***!
  \*********************************/
/***/ function(module, exports, __webpack_require__) {

	'use strict';
	
	var utils = __webpack_require__(/*! ./utils */ 177);
	var normalizeHeaderName = __webpack_require__(/*! ./helpers/normalizeHeaderName */ 178);
	
	var PROTECTION_PREFIX = /^\)\]\}',?\n/;
	var DEFAULT_CONTENT_TYPE = {
	  'Content-Type': 'application/x-www-form-urlencoded'
	};
	
	function setContentTypeIfUnset(headers, value) {
	  if (!utils.isUndefined(headers) && utils.isUndefined(headers['Content-Type'])) {
	    headers['Content-Type'] = value;
	  }
	}
	
	module.exports = {
	  transformRequest: [function transformRequest(data, headers) {
	    normalizeHeaderName(headers, 'Content-Type');
	    if (utils.isFormData(data) ||
	      utils.isArrayBuffer(data) ||
	      utils.isStream(data) ||
	      utils.isFile(data) ||
	      utils.isBlob(data)
	    ) {
	      return data;
	    }
	    if (utils.isArrayBufferView(data)) {
	      return data.buffer;
	    }
	    if (utils.isURLSearchParams(data)) {
	      setContentTypeIfUnset(headers, 'application/x-www-form-urlencoded;charset=utf-8');
	      return data.toString();
	    }
	    if (utils.isObject(data)) {
	      setContentTypeIfUnset(headers, 'application/json;charset=utf-8');
	      return JSON.stringify(data);
	    }
	    return data;
	  }],
	
	  transformResponse: [function transformResponse(data) {
	    /*eslint no-param-reassign:0*/
	    if (typeof data === 'string') {
	      data = data.replace(PROTECTION_PREFIX, '');
	      try {
	        data = JSON.parse(data);
	      } catch (e) { /* Ignore */ }
	    }
	    return data;
	  }],
	
	  headers: {
	    common: {
	      'Accept': 'application/json, text/plain, */*'
	    },
	    patch: utils.merge(DEFAULT_CONTENT_TYPE),
	    post: utils.merge(DEFAULT_CONTENT_TYPE),
	    put: utils.merge(DEFAULT_CONTENT_TYPE)
	  },
	
	  timeout: 0,
	
	  xsrfCookieName: 'XSRF-TOKEN',
	  xsrfHeaderName: 'X-XSRF-TOKEN',
	
	  maxContentLength: -1,
	
	  validateStatus: function validateStatus(status) {
	    return status >= 200 && status < 300;
	  }
	};


/***/ },

/***/ 177:
/*!******************************!*\
  !*** ./~/axios/lib/utils.js ***!
  \******************************/
/***/ function(module, exports) {

	'use strict';
	
	/*global toString:true*/
	
	// utils is a library of generic helper functions non-specific to axios
	
	var toString = Object.prototype.toString;
	
	/**
	 * Determine if a value is an Array
	 *
	 * @param {Object} val The value to test
	 * @returns {boolean} True if value is an Array, otherwise false
	 */
	function isArray(val) {
	  return toString.call(val) === '[object Array]';
	}
	
	/**
	 * Determine if a value is an ArrayBuffer
	 *
	 * @param {Object} val The value to test
	 * @returns {boolean} True if value is an ArrayBuffer, otherwise false
	 */
	function isArrayBuffer(val) {
	  return toString.call(val) === '[object ArrayBuffer]';
	}
	
	/**
	 * Determine if a value is a FormData
	 *
	 * @param {Object} val The value to test
	 * @returns {boolean} True if value is an FormData, otherwise false
	 */
	function isFormData(val) {
	  return (typeof FormData !== 'undefined') && (val instanceof FormData);
	}
	
	/**
	 * Determine if a value is a view on an ArrayBuffer
	 *
	 * @param {Object} val The value to test
	 * @returns {boolean} True if value is a view on an ArrayBuffer, otherwise false
	 */
	function isArrayBufferView(val) {
	  var result;
	  if ((typeof ArrayBuffer !== 'undefined') && (ArrayBuffer.isView)) {
	    result = ArrayBuffer.isView(val);
	  } else {
	    result = (val) && (val.buffer) && (val.buffer instanceof ArrayBuffer);
	  }
	  return result;
	}
	
	/**
	 * Determine if a value is a String
	 *
	 * @param {Object} val The value to test
	 * @returns {boolean} True if value is a String, otherwise false
	 */
	function isString(val) {
	  return typeof val === 'string';
	}
	
	/**
	 * Determine if a value is a Number
	 *
	 * @param {Object} val The value to test
	 * @returns {boolean} True if value is a Number, otherwise false
	 */
	function isNumber(val) {
	  return typeof val === 'number';
	}
	
	/**
	 * Determine if a value is undefined
	 *
	 * @param {Object} val The value to test
	 * @returns {boolean} True if the value is undefined, otherwise false
	 */
	function isUndefined(val) {
	  return typeof val === 'undefined';
	}
	
	/**
	 * Determine if a value is an Object
	 *
	 * @param {Object} val The value to test
	 * @returns {boolean} True if value is an Object, otherwise false
	 */
	function isObject(val) {
	  return val !== null && typeof val === 'object';
	}
	
	/**
	 * Determine if a value is a Date
	 *
	 * @param {Object} val The value to test
	 * @returns {boolean} True if value is a Date, otherwise false
	 */
	function isDate(val) {
	  return toString.call(val) === '[object Date]';
	}
	
	/**
	 * Determine if a value is a File
	 *
	 * @param {Object} val The value to test
	 * @returns {boolean} True if value is a File, otherwise false
	 */
	function isFile(val) {
	  return toString.call(val) === '[object File]';
	}
	
	/**
	 * Determine if a value is a Blob
	 *
	 * @param {Object} val The value to test
	 * @returns {boolean} True if value is a Blob, otherwise false
	 */
	function isBlob(val) {
	  return toString.call(val) === '[object Blob]';
	}
	
	/**
	 * Determine if a value is a Function
	 *
	 * @param {Object} val The value to test
	 * @returns {boolean} True if value is a Function, otherwise false
	 */
	function isFunction(val) {
	  return toString.call(val) === '[object Function]';
	}
	
	/**
	 * Determine if a value is a Stream
	 *
	 * @param {Object} val The value to test
	 * @returns {boolean} True if value is a Stream, otherwise false
	 */
	function isStream(val) {
	  return isObject(val) && isFunction(val.pipe);
	}
	
	/**
	 * Determine if a value is a URLSearchParams object
	 *
	 * @param {Object} val The value to test
	 * @returns {boolean} True if value is a URLSearchParams object, otherwise false
	 */
	function isURLSearchParams(val) {
	  return typeof URLSearchParams !== 'undefined' && val instanceof URLSearchParams;
	}
	
	/**
	 * Trim excess whitespace off the beginning and end of a string
	 *
	 * @param {String} str The String to trim
	 * @returns {String} The String freed of excess whitespace
	 */
	function trim(str) {
	  return str.replace(/^\s*/, '').replace(/\s*$/, '');
	}
	
	/**
	 * Determine if we're running in a standard browser environment
	 *
	 * This allows axios to run in a web worker, and react-native.
	 * Both environments support XMLHttpRequest, but not fully standard globals.
	 *
	 * web workers:
	 *  typeof window -> undefined
	 *  typeof document -> undefined
	 *
	 * react-native:
	 *  typeof document.createElement -> undefined
	 */
	function isStandardBrowserEnv() {
	  return (
	    typeof window !== 'undefined' &&
	    typeof document !== 'undefined' &&
	    typeof document.createElement === 'function'
	  );
	}
	
	/**
	 * Iterate over an Array or an Object invoking a function for each item.
	 *
	 * If `obj` is an Array callback will be called passing
	 * the value, index, and complete array for each item.
	 *
	 * If 'obj' is an Object callback will be called passing
	 * the value, key, and complete object for each property.
	 *
	 * @param {Object|Array} obj The object to iterate
	 * @param {Function} fn The callback to invoke for each item
	 */
	function forEach(obj, fn) {
	  // Don't bother if no value provided
	  if (obj === null || typeof obj === 'undefined') {
	    return;
	  }
	
	  // Force an array if not already something iterable
	  if (typeof obj !== 'object' && !isArray(obj)) {
	    /*eslint no-param-reassign:0*/
	    obj = [obj];
	  }
	
	  if (isArray(obj)) {
	    // Iterate over array values
	    for (var i = 0, l = obj.length; i < l; i++) {
	      fn.call(null, obj[i], i, obj);
	    }
	  } else {
	    // Iterate over object keys
	    for (var key in obj) {
	      if (obj.hasOwnProperty(key)) {
	        fn.call(null, obj[key], key, obj);
	      }
	    }
	  }
	}
	
	/**
	 * Accepts varargs expecting each argument to be an object, then
	 * immutably merges the properties of each object and returns result.
	 *
	 * When multiple objects contain the same key the later object in
	 * the arguments list will take precedence.
	 *
	 * Example:
	 *
	 * ```js
	 * var result = merge({foo: 123}, {foo: 456});
	 * console.log(result.foo); // outputs 456
	 * ```
	 *
	 * @param {Object} obj1 Object to merge
	 * @returns {Object} Result of all merge properties
	 */
	function merge(/* obj1, obj2, obj3, ... */) {
	  var result = {};
	  function assignValue(val, key) {
	    if (typeof result[key] === 'object' && typeof val === 'object') {
	      result[key] = merge(result[key], val);
	    } else {
	      result[key] = val;
	    }
	  }
	
	  for (var i = 0, l = arguments.length; i < l; i++) {
	    forEach(arguments[i], assignValue);
	  }
	  return result;
	}
	
	module.exports = {
	  isArray: isArray,
	  isArrayBuffer: isArrayBuffer,
	  isFormData: isFormData,
	  isArrayBufferView: isArrayBufferView,
	  isString: isString,
	  isNumber: isNumber,
	  isObject: isObject,
	  isUndefined: isUndefined,
	  isDate: isDate,
	  isFile: isFile,
	  isBlob: isBlob,
	  isFunction: isFunction,
	  isStream: isStream,
	  isURLSearchParams: isURLSearchParams,
	  isStandardBrowserEnv: isStandardBrowserEnv,
	  forEach: forEach,
	  merge: merge,
	  trim: trim
	};


/***/ },

/***/ 178:
/*!****************************************************!*\
  !*** ./~/axios/lib/helpers/normalizeHeaderName.js ***!
  \****************************************************/
/***/ function(module, exports, __webpack_require__) {

	'use strict';
	
	var utils = __webpack_require__(/*! ../utils */ 177);
	
	module.exports = function normalizeHeaderName(headers, normalizedName) {
	  utils.forEach(headers, function processHeader(value, name) {
	    if (name !== normalizedName && name.toUpperCase() === normalizedName.toUpperCase()) {
	      headers[normalizedName] = value;
	      delete headers[name];
	    }
	  });
	};


/***/ },

/***/ 179:
/*!*********************************************!*\
  !*** ./~/axios/lib/core/dispatchRequest.js ***!
  \*********************************************/
/***/ function(module, exports, __webpack_require__) {

	/* WEBPACK VAR INJECTION */(function(process) {'use strict';
	
	/**
	 * Dispatch a request to the server using whichever adapter
	 * is supported by the current environment.
	 *
	 * @param {object} config The config that is to be used for the request
	 * @returns {Promise} The Promise to be fulfilled
	 */
	module.exports = function dispatchRequest(config) {
	  return new Promise(function executor(resolve, reject) {
	    try {
	      var adapter;
	
	      if (typeof config.adapter === 'function') {
	        // For custom adapter support
	        adapter = config.adapter;
	      } else if (typeof XMLHttpRequest !== 'undefined') {
	        // For browsers use XHR adapter
	        adapter = __webpack_require__(/*! ../adapters/xhr */ 180);
	      } else if (typeof process !== 'undefined') {
	        // For node use HTTP adapter
	        adapter = __webpack_require__(/*! ../adapters/http */ 180);
	      }
	
	      if (typeof adapter === 'function') {
	        adapter(resolve, reject, config);
	      }
	    } catch (e) {
	      reject(e);
	    }
	  });
	};
	
	
	/* WEBPACK VAR INJECTION */}.call(exports, __webpack_require__(/*! ./~/process/browser.js */ 3)))

/***/ },

/***/ 180:
/*!*************************************!*\
  !*** ./~/axios/lib/adapters/xhr.js ***!
  \*************************************/
/***/ function(module, exports, __webpack_require__) {

	/* WEBPACK VAR INJECTION */(function(process) {'use strict';
	
	var utils = __webpack_require__(/*! ./../utils */ 177);
	var buildURL = __webpack_require__(/*! ./../helpers/buildURL */ 181);
	var parseHeaders = __webpack_require__(/*! ./../helpers/parseHeaders */ 182);
	var transformData = __webpack_require__(/*! ./../helpers/transformData */ 183);
	var isURLSameOrigin = __webpack_require__(/*! ./../helpers/isURLSameOrigin */ 184);
	var btoa = (typeof window !== 'undefined' && window.btoa) || __webpack_require__(/*! ./../helpers/btoa */ 185);
	var settle = __webpack_require__(/*! ../helpers/settle */ 186);
	
	module.exports = function xhrAdapter(resolve, reject, config) {
	  var requestData = config.data;
	  var requestHeaders = config.headers;
	
	  if (utils.isFormData(requestData)) {
	    delete requestHeaders['Content-Type']; // Let the browser set it
	  }
	
	  var request = new XMLHttpRequest();
	  var loadEvent = 'onreadystatechange';
	  var xDomain = false;
	
	  // For IE 8/9 CORS support
	  // Only supports POST and GET calls and doesn't returns the response headers.
	  // DON'T do this for testing b/c XMLHttpRequest is mocked, not XDomainRequest.
	  if (process.env.NODE_ENV !== 'test' && typeof window !== 'undefined' && window.XDomainRequest && !('withCredentials' in request) && !isURLSameOrigin(config.url)) {
	    request = new window.XDomainRequest();
	    loadEvent = 'onload';
	    xDomain = true;
	    request.onprogress = function handleProgress() {};
	    request.ontimeout = function handleTimeout() {};
	  }
	
	  // HTTP basic authentication
	  if (config.auth) {
	    var username = config.auth.username || '';
	    var password = config.auth.password || '';
	    requestHeaders.Authorization = 'Basic ' + btoa(username + ':' + password);
	  }
	
	  request.open(config.method.toUpperCase(), buildURL(config.url, config.params, config.paramsSerializer), true);
	
	  // Set the request timeout in MS
	  request.timeout = config.timeout;
	
	  // Listen for ready state
	  request[loadEvent] = function handleLoad() {
	    if (!request || (request.readyState !== 4 && !xDomain)) {
	      return;
	    }
	
	    // The request errored out and we didn't get a response, this will be
	    // handled by onerror instead
	    if (request.status === 0) {
	      return;
	    }
	
	    // Prepare the response
	    var responseHeaders = 'getAllResponseHeaders' in request ? parseHeaders(request.getAllResponseHeaders()) : null;
	    var responseData = !config.responseType || config.responseType === 'text' ? request.responseText : request.response;
	    var response = {
	      data: transformData(
	        responseData,
	        responseHeaders,
	        config.transformResponse
	      ),
	      // IE sends 1223 instead of 204 (https://github.com/mzabriskie/axios/issues/201)
	      status: request.status === 1223 ? 204 : request.status,
	      statusText: request.status === 1223 ? 'No Content' : request.statusText,
	      headers: responseHeaders,
	      config: config,
	      request: request
	    };
	
	    settle(resolve, reject, response);
	
	    // Clean up request
	    request = null;
	  };
	
	  // Handle low level network errors
	  request.onerror = function handleError() {
	    // Real errors are hidden from us by the browser
	    // onerror should only fire if it's a network error
	    reject(new Error('Network Error'));
	
	    // Clean up request
	    request = null;
	  };
	
	  // Handle timeout
	  request.ontimeout = function handleTimeout() {
	    var err = new Error('timeout of ' + config.timeout + 'ms exceeded');
	    err.timeout = config.timeout;
	    err.code = 'ECONNABORTED';
	    reject(err);
	
	    // Clean up request
	    request = null;
	  };
	
	  // Add xsrf header
	  // This is only done if running in a standard browser environment.
	  // Specifically not if we're in a web worker, or react-native.
	  if (utils.isStandardBrowserEnv()) {
	    var cookies = __webpack_require__(/*! ./../helpers/cookies */ 187);
	
	    // Add xsrf header
	    var xsrfValue = config.withCredentials || isURLSameOrigin(config.url) ?
	        cookies.read(config.xsrfCookieName) :
	        undefined;
	
	    if (xsrfValue) {
	      requestHeaders[config.xsrfHeaderName] = xsrfValue;
	    }
	  }
	
	  // Add headers to the request
	  if ('setRequestHeader' in request) {
	    utils.forEach(requestHeaders, function setRequestHeader(val, key) {
	      if (typeof requestData === 'undefined' && key.toLowerCase() === 'content-type') {
	        // Remove Content-Type if data is undefined
	        delete requestHeaders[key];
	      } else {
	        // Otherwise add header to the request
	        request.setRequestHeader(key, val);
	      }
	    });
	  }
	
	  // Add withCredentials to request if needed
	  if (config.withCredentials) {
	    request.withCredentials = true;
	  }
	
	  // Add responseType to request if needed
	  if (config.responseType) {
	    try {
	      request.responseType = config.responseType;
	    } catch (e) {
	      if (request.responseType !== 'json') {
	        throw e;
	      }
	    }
	  }
	
	  // Handle progress if needed
	  if (config.progress) {
	    if (config.method === 'post' || config.method === 'put') {
	      request.upload.addEventListener('progress', config.progress);
	    } else if (config.method === 'get') {
	      request.addEventListener('progress', config.progress);
	    }
	  }
	
	  if (requestData === undefined) {
	    requestData = null;
	  }
	
	  // Send the request
	  request.send(requestData);
	};
	
	/* WEBPACK VAR INJECTION */}.call(exports, __webpack_require__(/*! ./~/process/browser.js */ 3)))

/***/ },

/***/ 181:
/*!*****************************************!*\
  !*** ./~/axios/lib/helpers/buildURL.js ***!
  \*****************************************/
/***/ function(module, exports, __webpack_require__) {

	'use strict';
	
	var utils = __webpack_require__(/*! ./../utils */ 177);
	
	function encode(val) {
	  return encodeURIComponent(val).
	    replace(/%40/gi, '@').
	    replace(/%3A/gi, ':').
	    replace(/%24/g, '$').
	    replace(/%2C/gi, ',').
	    replace(/%20/g, '+').
	    replace(/%5B/gi, '[').
	    replace(/%5D/gi, ']');
	}
	
	/**
	 * Build a URL by appending params to the end
	 *
	 * @param {string} url The base of the url (e.g., http://www.google.com)
	 * @param {object} [params] The params to be appended
	 * @returns {string} The formatted url
	 */
	module.exports = function buildURL(url, params, paramsSerializer) {
	  /*eslint no-param-reassign:0*/
	  if (!params) {
	    return url;
	  }
	
	  var serializedParams;
	  if (paramsSerializer) {
	    serializedParams = paramsSerializer(params);
	  } else if (utils.isURLSearchParams(params)) {
	    serializedParams = params.toString();
	  } else {
	    var parts = [];
	
	    utils.forEach(params, function serialize(val, key) {
	      if (val === null || typeof val === 'undefined') {
	        return;
	      }
	
	      if (utils.isArray(val)) {
	        key = key + '[]';
	      }
	
	      if (!utils.isArray(val)) {
	        val = [val];
	      }
	
	      utils.forEach(val, function parseValue(v) {
	        if (utils.isDate(v)) {
	          v = v.toISOString();
	        } else if (utils.isObject(v)) {
	          v = JSON.stringify(v);
	        }
	        parts.push(encode(key) + '=' + encode(v));
	      });
	    });
	
	    serializedParams = parts.join('&');
	  }
	
	  if (serializedParams) {
	    url += (url.indexOf('?') === -1 ? '?' : '&') + serializedParams;
	  }
	
	  return url;
	};


/***/ },

/***/ 182:
/*!*********************************************!*\
  !*** ./~/axios/lib/helpers/parseHeaders.js ***!
  \*********************************************/
/***/ function(module, exports, __webpack_require__) {

	'use strict';
	
	var utils = __webpack_require__(/*! ./../utils */ 177);
	
	/**
	 * Parse headers into an object
	 *
	 * ```
	 * Date: Wed, 27 Aug 2014 08:58:49 GMT
	 * Content-Type: application/json
	 * Connection: keep-alive
	 * Transfer-Encoding: chunked
	 * ```
	 *
	 * @param {String} headers Headers needing to be parsed
	 * @returns {Object} Headers parsed into an object
	 */
	module.exports = function parseHeaders(headers) {
	  var parsed = {};
	  var key;
	  var val;
	  var i;
	
	  if (!headers) { return parsed; }
	
	  utils.forEach(headers.split('\n'), function parser(line) {
	    i = line.indexOf(':');
	    key = utils.trim(line.substr(0, i)).toLowerCase();
	    val = utils.trim(line.substr(i + 1));
	
	    if (key) {
	      parsed[key] = parsed[key] ? parsed[key] + ', ' + val : val;
	    }
	  });
	
	  return parsed;
	};


/***/ },

/***/ 183:
/*!**********************************************!*\
  !*** ./~/axios/lib/helpers/transformData.js ***!
  \**********************************************/
/***/ function(module, exports, __webpack_require__) {

	'use strict';
	
	var utils = __webpack_require__(/*! ./../utils */ 177);
	
	/**
	 * Transform the data for a request or a response
	 *
	 * @param {Object|String} data The data to be transformed
	 * @param {Array} headers The headers for the request or response
	 * @param {Array|Function} fns A single function or Array of functions
	 * @returns {*} The resulting transformed data
	 */
	module.exports = function transformData(data, headers, fns) {
	  /*eslint no-param-reassign:0*/
	  utils.forEach(fns, function transform(fn) {
	    data = fn(data, headers);
	  });
	
	  return data;
	};


/***/ },

/***/ 184:
/*!************************************************!*\
  !*** ./~/axios/lib/helpers/isURLSameOrigin.js ***!
  \************************************************/
/***/ function(module, exports, __webpack_require__) {

	'use strict';
	
	var utils = __webpack_require__(/*! ./../utils */ 177);
	
	module.exports = (
	  utils.isStandardBrowserEnv() ?
	
	  // Standard browser envs have full support of the APIs needed to test
	  // whether the request URL is of the same origin as current location.
	  (function standardBrowserEnv() {
	    var msie = /(msie|trident)/i.test(navigator.userAgent);
	    var urlParsingNode = document.createElement('a');
	    var originURL;
	
	    /**
	    * Parse a URL to discover it's components
	    *
	    * @param {String} url The URL to be parsed
	    * @returns {Object}
	    */
	    function resolveURL(url) {
	      var href = url;
	
	      if (msie) {
	        // IE needs attribute set twice to normalize properties
	        urlParsingNode.setAttribute('href', href);
	        href = urlParsingNode.href;
	      }
	
	      urlParsingNode.setAttribute('href', href);
	
	      // urlParsingNode provides the UrlUtils interface - http://url.spec.whatwg.org/#urlutils
	      return {
	        href: urlParsingNode.href,
	        protocol: urlParsingNode.protocol ? urlParsingNode.protocol.replace(/:$/, '') : '',
	        host: urlParsingNode.host,
	        search: urlParsingNode.search ? urlParsingNode.search.replace(/^\?/, '') : '',
	        hash: urlParsingNode.hash ? urlParsingNode.hash.replace(/^#/, '') : '',
	        hostname: urlParsingNode.hostname,
	        port: urlParsingNode.port,
	        pathname: (urlParsingNode.pathname.charAt(0) === '/') ?
	                  urlParsingNode.pathname :
	                  '/' + urlParsingNode.pathname
	      };
	    }
	
	    originURL = resolveURL(window.location.href);
	
	    /**
	    * Determine if a URL shares the same origin as the current location
	    *
	    * @param {String} requestURL The URL to test
	    * @returns {boolean} True if URL shares the same origin, otherwise false
	    */
	    return function isURLSameOrigin(requestURL) {
	      var parsed = (utils.isString(requestURL)) ? resolveURL(requestURL) : requestURL;
	      return (parsed.protocol === originURL.protocol &&
	            parsed.host === originURL.host);
	    };
	  })() :
	
	  // Non standard browser envs (web workers, react-native) lack needed support.
	  (function nonStandardBrowserEnv() {
	    return function isURLSameOrigin() {
	      return true;
	    };
	  })()
	);


/***/ },

/***/ 185:
/*!*************************************!*\
  !*** ./~/axios/lib/helpers/btoa.js ***!
  \*************************************/
/***/ function(module, exports) {

	'use strict';
	
	// btoa polyfill for IE<10 courtesy https://github.com/davidchambers/Base64.js
	
	var chars = 'ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz0123456789+/=';
	
	function E() {
	  this.message = 'String contains an invalid character';
	}
	E.prototype = new Error;
	E.prototype.code = 5;
	E.prototype.name = 'InvalidCharacterError';
	
	function btoa(input) {
	  var str = String(input);
	  var output = '';
	  for (
	    // initialize result and counter
	    var block, charCode, idx = 0, map = chars;
	    // if the next str index does not exist:
	    //   change the mapping table to "="
	    //   check if d has no fractional digits
	    str.charAt(idx | 0) || (map = '=', idx % 1);
	    // "8 - idx % 1 * 8" generates the sequence 2, 4, 6, 8
	    output += map.charAt(63 & block >> 8 - idx % 1 * 8)
	  ) {
	    charCode = str.charCodeAt(idx += 3 / 4);
	    if (charCode > 0xFF) {
	      throw new E();
	    }
	    block = block << 8 | charCode;
	  }
	  return output;
	}
	
	module.exports = btoa;


/***/ },

/***/ 186:
/*!***************************************!*\
  !*** ./~/axios/lib/helpers/settle.js ***!
  \***************************************/
/***/ function(module, exports) {

	'use strict';
	
	/**
	 * Resolve or reject a Promise based on response status.
	 *
	 * @param {Function} resolve A function that resolves the promise.
	 * @param {Function} reject A function that rejects the promise.
	 * @param {object} response The response.
	 */
	module.exports = function settle(resolve, reject, response) {
	  var validateStatus = response.config.validateStatus;
	  // Note: status is not exposed by XDomainRequest
	  if (!response.status || !validateStatus || validateStatus(response.status)) {
	    resolve(response);
	  } else {
	    reject(response);
	  }
	};


/***/ },

/***/ 187:
/*!****************************************!*\
  !*** ./~/axios/lib/helpers/cookies.js ***!
  \****************************************/
/***/ function(module, exports, __webpack_require__) {

	'use strict';
	
	var utils = __webpack_require__(/*! ./../utils */ 177);
	
	module.exports = (
	  utils.isStandardBrowserEnv() ?
	
	  // Standard browser envs support document.cookie
	  (function standardBrowserEnv() {
	    return {
	      write: function write(name, value, expires, path, domain, secure) {
	        var cookie = [];
	        cookie.push(name + '=' + encodeURIComponent(value));
	
	        if (utils.isNumber(expires)) {
	          cookie.push('expires=' + new Date(expires).toGMTString());
	        }
	
	        if (utils.isString(path)) {
	          cookie.push('path=' + path);
	        }
	
	        if (utils.isString(domain)) {
	          cookie.push('domain=' + domain);
	        }
	
	        if (secure === true) {
	          cookie.push('secure');
	        }
	
	        document.cookie = cookie.join('; ');
	      },
	
	      read: function read(name) {
	        var match = document.cookie.match(new RegExp('(^|;\\s*)(' + name + ')=([^;]*)'));
	        return (match ? decodeURIComponent(match[3]) : null);
	      },
	
	      remove: function remove(name) {
	        this.write(name, '', Date.now() - 86400000);
	      }
	    };
	  })() :
	
	  // Non standard browser env (web workers, react-native) lack needed support.
	  (function nonStandardBrowserEnv() {
	    return {
	      write: function write() {},
	      read: function read() { return null; },
	      remove: function remove() {}
	    };
	  })()
	);


/***/ },

/***/ 188:
/*!************************************************!*\
  !*** ./~/axios/lib/core/InterceptorManager.js ***!
  \************************************************/
/***/ function(module, exports, __webpack_require__) {

	'use strict';
	
	var utils = __webpack_require__(/*! ./../utils */ 177);
	
	function InterceptorManager() {
	  this.handlers = [];
	}
	
	/**
	 * Add a new interceptor to the stack
	 *
	 * @param {Function} fulfilled The function to handle `then` for a `Promise`
	 * @param {Function} rejected The function to handle `reject` for a `Promise`
	 *
	 * @return {Number} An ID used to remove interceptor later
	 */
	InterceptorManager.prototype.use = function use(fulfilled, rejected) {
	  this.handlers.push({
	    fulfilled: fulfilled,
	    rejected: rejected
	  });
	  return this.handlers.length - 1;
	};
	
	/**
	 * Remove an interceptor from the stack
	 *
	 * @param {Number} id The ID that was returned by `use`
	 */
	InterceptorManager.prototype.eject = function eject(id) {
	  if (this.handlers[id]) {
	    this.handlers[id] = null;
	  }
	};
	
	/**
	 * Iterate over all the registered interceptors
	 *
	 * This method is particularly useful for skipping over any
	 * interceptors that may have become `null` calling `eject`.
	 *
	 * @param {Function} fn The function to call for each interceptor
	 */
	InterceptorManager.prototype.forEach = function forEach(fn) {
	  utils.forEach(this.handlers, function forEachHandler(h) {
	    if (h !== null) {
	      fn(h);
	    }
	  });
	};
	
	module.exports = InterceptorManager;


/***/ },

/***/ 189:
/*!**********************************************!*\
  !*** ./~/axios/lib/helpers/isAbsoluteURL.js ***!
  \**********************************************/
/***/ function(module, exports) {

	'use strict';
	
	/**
	 * Determines whether the specified URL is absolute
	 *
	 * @param {string} url The URL to test
	 * @returns {boolean} True if the specified URL is absolute, otherwise false
	 */
	module.exports = function isAbsoluteURL(url) {
	  // A URL is considered absolute if it begins with "<scheme>://" or "//" (protocol-relative URL).
	  // RFC 3986 defines scheme name as a sequence of characters beginning with a letter and followed
	  // by any combination of letters, digits, plus, period, or hyphen.
	  return /^([a-z][a-z\d\+\-\.]*:)?\/\//i.test(url);
	};


/***/ },

/***/ 190:
/*!********************************************!*\
  !*** ./~/axios/lib/helpers/combineURLs.js ***!
  \********************************************/
/***/ function(module, exports) {

	'use strict';
	
	/**
	 * Creates a new URL by combining the specified URLs
	 *
	 * @param {string} baseURL The base URL
	 * @param {string} relativeURL The relative URL
	 * @returns {string} The combined URL
	 */
	module.exports = function combineURLs(baseURL, relativeURL) {
	  return baseURL.replace(/\/+$/, '') + '/' + relativeURL.replace(/^\/+/, '');
	};


/***/ },

/***/ 191:
/*!*************************************!*\
  !*** ./~/axios/lib/helpers/bind.js ***!
  \*************************************/
/***/ function(module, exports) {

	'use strict';
	
	module.exports = function bind(fn, thisArg) {
	  return function wrap() {
	    var args = new Array(arguments.length);
	    for (var i = 0; i < args.length; i++) {
	      args[i] = arguments[i];
	    }
	    return fn.apply(thisArg, args);
	  };
	};


/***/ },

/***/ 192:
/*!***************************************!*\
  !*** ./~/axios/lib/helpers/spread.js ***!
  \***************************************/
/***/ function(module, exports) {

	'use strict';
	
	/**
	 * Syntactic sugar for invoking a function and expanding an array for arguments.
	 *
	 * Common use case would be to use `Function.prototype.apply`.
	 *
	 *  ```js
	 *  function f(x, y, z) {}
	 *  var args = [1, 2, 3];
	 *  f.apply(null, args);
	 *  ```
	 *
	 * With `spread` this example can be re-written.
	 *
	 *  ```js
	 *  spread(function(x, y, z) {})([1, 2, 3]);
	 *  ```
	 *
	 * @param {Function} callback
	 * @returns {Function}
	 */
	module.exports = function spread(callback) {
	  return function wrap(arr) {
	    return callback.apply(null, arr);
	  };
	};


/***/ },

/***/ 193:
/*!*******************************************************************************!*\
  !*** external "{\"apiUrl\":\"http://accountgo-dev-api.azurewebsites.net/\"}" ***!
  \*******************************************************************************/
/***/ function(module, exports) {

	module.exports = {"apiUrl":"http://accountgo-dev-api.azurewebsites.net/"};

/***/ },

/***/ 196:
/*!*******************************************************************!*\
  !*** ./wwwroot/libs/tsxbuild/Shared/Stores/Common/CommonStore.js ***!
  \*******************************************************************/
/***/ function(module, exports, __webpack_require__) {

	"use strict";
	var __decorate = (this && this.__decorate) || function (decorators, target, key, desc) {
	    var c = arguments.length, r = c < 3 ? target : desc === null ? desc = Object.getOwnPropertyDescriptor(target, key) : desc, d;
	    if (typeof Reflect === "object" && typeof Reflect.decorate === "function") r = Reflect.decorate(decorators, target, key, desc);
	    else for (var i = decorators.length - 1; i >= 0; i--) if (d = decorators[i]) r = (c < 3 ? d(r) : c > 3 ? d(target, key, r) : d(target, key)) || r;
	    return c > 3 && r && Object.defineProperty(target, key, r), r;
	};
	var mobx_1 = __webpack_require__(/*! mobx */ 169);
	var axios = __webpack_require__(/*! axios */ 174);
	var Config = __webpack_require__(/*! Config */ 193);
	var CommonStore = (function () {
	    function CommonStore() {
	        this.customers = [];
	        this.paymentTerms = [];
	        this.items = [];
	        this.measurements = [];
	        this.vendors = [];
	        this.accounts = [];
	        this.loadCustomersLookup();
	        this.loadPaymentTermsLookup();
	        this.loadItemsLookup();
	        this.loadMeasurementsLookup();
	        this.loadVendorsLookup();
	        this.loadAccountsLookup();
	    }
	    CommonStore.prototype.loadCustomersLookup = function () {
	        var customers = this.customers;
	        axios.get(Config.apiUrl + "api/common/customers")
	            .then(function (result) {
	            var data = result.data;
	            for (var i = 0; i < data.length; i++) {
	                customers.push(data[i]);
	            }
	        });
	    };
	    CommonStore.prototype.loadPaymentTermsLookup = function () {
	        var paymentTerms = this.paymentTerms;
	        axios.get(Config.apiUrl + "api/common/paymentterms")
	            .then(function (result) {
	            var data = result.data;
	            for (var i = 0; i < data.length; i++) {
	                paymentTerms.push(data[i]);
	            }
	        });
	    };
	    CommonStore.prototype.loadVendorsLookup = function () {
	        var vendors = this.vendors;
	        axios.get(Config.apiUrl + "api/common/vendors")
	            .then(function (result) {
	            var data = result.data;
	            for (var i = 0; i < data.length; i++) {
	                vendors.push(data[i]);
	            }
	        });
	    };
	    CommonStore.prototype.loadItemsLookup = function () {
	        var items = this.items;
	        axios.get(Config.apiUrl + "api/common/items")
	            .then(function (result) {
	            var data = result.data;
	            for (var i = 0; i < data.length; i++) {
	                items.push(data[i]);
	            }
	        });
	    };
	    CommonStore.prototype.loadMeasurementsLookup = function () {
	        var measurements = this.measurements;
	        axios.get(Config.apiUrl + "api/common/measurements")
	            .then(function (result) {
	            var data = result.data;
	            for (var i = 0; i < data.length; i++) {
	                measurements.push(data[i]);
	            }
	        });
	    };
	    CommonStore.prototype.loadVoucherTypesLookup = function () {
	    };
	    CommonStore.prototype.loadAccountsLookup = function () {
	        var accounts = this.accounts;
	        axios.get(Config.apiUrl + "api/common/accounts")
	            .then(function (result) {
	            var data = result.data;
	            for (var i = 0; i < data.length; i++) {
	                accounts.push(data[i]);
	            }
	        });
	    };
	    __decorate([
	        mobx_1.observable
	    ], CommonStore.prototype, "customers", void 0);
	    __decorate([
	        mobx_1.observable
	    ], CommonStore.prototype, "paymentTerms", void 0);
	    __decorate([
	        mobx_1.observable
	    ], CommonStore.prototype, "items", void 0);
	    __decorate([
	        mobx_1.observable
	    ], CommonStore.prototype, "measurements", void 0);
	    __decorate([
	        mobx_1.observable
	    ], CommonStore.prototype, "vendors", void 0);
	    __decorate([
	        mobx_1.observable
	    ], CommonStore.prototype, "accounts", void 0);
	    return CommonStore;
	}());
	Object.defineProperty(exports, "__esModule", { value: true });
	exports.default = CommonStore;
	//# sourceMappingURL=CommonStore.js.map

/***/ },

/***/ 198:
/*!**********************************************************************!*\
  !*** ./wwwroot/libs/tsxbuild/Shared/Components/SelectPaymentTerm.js ***!
  \**********************************************************************/
/***/ function(module, exports, __webpack_require__) {

	"use strict";
	var __extends = (this && this.__extends) || function (d, b) {
	    for (var p in b) if (b.hasOwnProperty(p)) d[p] = b[p];
	    function __() { this.constructor = d; }
	    d.prototype = b === null ? Object.create(b) : (__.prototype = b.prototype, new __());
	};
	var __decorate = (this && this.__decorate) || function (decorators, target, key, desc) {
	    var c = arguments.length, r = c < 3 ? target : desc === null ? desc = Object.getOwnPropertyDescriptor(target, key) : desc, d;
	    if (typeof Reflect === "object" && typeof Reflect.decorate === "function") r = Reflect.decorate(decorators, target, key, desc);
	    else for (var i = decorators.length - 1; i >= 0; i--) if (d = decorators[i]) r = (c < 3 ? d(r) : c > 3 ? d(target, key, r) : d(target, key)) || r;
	    return c > 3 && r && Object.defineProperty(target, key, r), r;
	};
	var React = __webpack_require__(/*! react */ 1);
	var mobx_react_1 = __webpack_require__(/*! mobx-react */ 168);
	var SelectPaymentTerm = (function (_super) {
	    __extends(SelectPaymentTerm, _super);
	    function SelectPaymentTerm() {
	        _super.apply(this, arguments);
	    }
	    SelectPaymentTerm.prototype.onChangePaymentTerm = function (e) {
	        this.props.store.changedPaymentTerm(e.target.value);
	    };
	    SelectPaymentTerm.prototype.render = function () {
	        var options = [];
	        this.props.store.commonStore.paymentTerms.map(function (term) {
	            return (options.push(React.createElement("option", {key: term.id, value: term.id}, " ", term.description, " ")));
	        });
	        return (React.createElement("select", {id: "optPaymentTerm", onChange: this.onChangePaymentTerm.bind(this), className: "form-control select2"}, options));
	    };
	    SelectPaymentTerm = __decorate([
	        mobx_react_1.observer
	    ], SelectPaymentTerm);
	    return SelectPaymentTerm;
	}(React.Component));
	Object.defineProperty(exports, "__esModule", { value: true });
	exports.default = SelectPaymentTerm;
	//# sourceMappingURL=SelectPaymentTerm.js.map

/***/ },

/***/ 199:
/*!*******************************************************************!*\
  !*** ./wwwroot/libs/tsxbuild/Shared/Components/SelectLineItem.js ***!
  \*******************************************************************/
/***/ function(module, exports, __webpack_require__) {

	"use strict";
	var __extends = (this && this.__extends) || function (d, b) {
	    for (var p in b) if (b.hasOwnProperty(p)) d[p] = b[p];
	    function __() { this.constructor = d; }
	    d.prototype = b === null ? Object.create(b) : (__.prototype = b.prototype, new __());
	};
	var __decorate = (this && this.__decorate) || function (decorators, target, key, desc) {
	    var c = arguments.length, r = c < 3 ? target : desc === null ? desc = Object.getOwnPropertyDescriptor(target, key) : desc, d;
	    if (typeof Reflect === "object" && typeof Reflect.decorate === "function") r = Reflect.decorate(decorators, target, key, desc);
	    else for (var i = decorators.length - 1; i >= 0; i--) if (d = decorators[i]) r = (c < 3 ? d(r) : c > 3 ? d(target, key, r) : d(target, key)) || r;
	    return c > 3 && r && Object.defineProperty(target, key, r), r;
	};
	var React = __webpack_require__(/*! react */ 1);
	var mobx_react_1 = __webpack_require__(/*! mobx-react */ 168);
	var SelectLineItem = (function (_super) {
	    __extends(SelectLineItem, _super);
	    function SelectLineItem() {
	        _super.apply(this, arguments);
	    }
	    SelectLineItem.prototype.onChangeItem = function (e) {
	        if (this.props.row !== undefined)
	            this.props.store.updateLineItem(this.props.row, "itemId", e.target.value);
	    };
	    SelectLineItem.prototype.render = function () {
	        var options = [];
	        this.props.store.commonStore.items.map(function (item) {
	            return (options.push(React.createElement("option", {key: item.id, value: item.id}, " ", item.description, " ")));
	        });
	        return (React.createElement("select", {defaultValue: this.props.selected, id: this.props.controlId, onChange: this.onChangeItem.bind(this), className: "form-control select2"}, options));
	    };
	    SelectLineItem = __decorate([
	        mobx_react_1.observer
	    ], SelectLineItem);
	    return SelectLineItem;
	}(React.Component));
	Object.defineProperty(exports, "__esModule", { value: true });
	exports.default = SelectLineItem;
	//# sourceMappingURL=SelectLineItem.js.map

/***/ },

/***/ 200:
/*!**************************************************************************!*\
  !*** ./wwwroot/libs/tsxbuild/Shared/Components/SelectLineMeasurement.js ***!
  \**************************************************************************/
/***/ function(module, exports, __webpack_require__) {

	"use strict";
	var __extends = (this && this.__extends) || function (d, b) {
	    for (var p in b) if (b.hasOwnProperty(p)) d[p] = b[p];
	    function __() { this.constructor = d; }
	    d.prototype = b === null ? Object.create(b) : (__.prototype = b.prototype, new __());
	};
	var __decorate = (this && this.__decorate) || function (decorators, target, key, desc) {
	    var c = arguments.length, r = c < 3 ? target : desc === null ? desc = Object.getOwnPropertyDescriptor(target, key) : desc, d;
	    if (typeof Reflect === "object" && typeof Reflect.decorate === "function") r = Reflect.decorate(decorators, target, key, desc);
	    else for (var i = decorators.length - 1; i >= 0; i--) if (d = decorators[i]) r = (c < 3 ? d(r) : c > 3 ? d(target, key, r) : d(target, key)) || r;
	    return c > 3 && r && Object.defineProperty(target, key, r), r;
	};
	var React = __webpack_require__(/*! react */ 1);
	var mobx_react_1 = __webpack_require__(/*! mobx-react */ 168);
	var SelectLineMeasurement = (function (_super) {
	    __extends(SelectLineMeasurement, _super);
	    function SelectLineMeasurement() {
	        _super.apply(this, arguments);
	    }
	    SelectLineMeasurement.prototype.onChangeMeasurement = function (e) {
	        if (this.props.row !== undefined)
	            this.props.store.updateLineItem(this.props.row, "measurementId", e.target.value);
	    };
	    SelectLineMeasurement.prototype.render = function () {
	        var options = [];
	        this.props.store.commonStore.measurements.map(function (measurement) {
	            return (options.push(React.createElement("option", {key: measurement.id, value: measurement.id}, " ", measurement.description, " ")));
	        });
	        return (React.createElement("select", {defaultValue: this.props.selected, id: this.props.controlId, onChange: this.onChangeMeasurement.bind(this), className: "form-control select2"}, options));
	    };
	    SelectLineMeasurement = __decorate([
	        mobx_react_1.observer
	    ], SelectLineMeasurement);
	    return SelectLineMeasurement;
	}(React.Component));
	Object.defineProperty(exports, "__esModule", { value: true });
	exports.default = SelectLineMeasurement;
	//# sourceMappingURL=SelectLineMeasurement.js.map

/***/ },

/***/ 207:
/*!*******************************************************************!*\
  !*** ./wwwroot/libs/tsxbuild/Shared/Components/SelectCustomer.js ***!
  \*******************************************************************/
/***/ function(module, exports, __webpack_require__) {

	"use strict";
	var __extends = (this && this.__extends) || function (d, b) {
	    for (var p in b) if (b.hasOwnProperty(p)) d[p] = b[p];
	    function __() { this.constructor = d; }
	    d.prototype = b === null ? Object.create(b) : (__.prototype = b.prototype, new __());
	};
	var __decorate = (this && this.__decorate) || function (decorators, target, key, desc) {
	    var c = arguments.length, r = c < 3 ? target : desc === null ? desc = Object.getOwnPropertyDescriptor(target, key) : desc, d;
	    if (typeof Reflect === "object" && typeof Reflect.decorate === "function") r = Reflect.decorate(decorators, target, key, desc);
	    else for (var i = decorators.length - 1; i >= 0; i--) if (d = decorators[i]) r = (c < 3 ? d(r) : c > 3 ? d(target, key, r) : d(target, key)) || r;
	    return c > 3 && r && Object.defineProperty(target, key, r), r;
	};
	var React = __webpack_require__(/*! react */ 1);
	var mobx_react_1 = __webpack_require__(/*! mobx-react */ 168);
	var SelectCustomer = (function (_super) {
	    __extends(SelectCustomer, _super);
	    function SelectCustomer() {
	        _super.apply(this, arguments);
	    }
	    SelectCustomer.prototype.onChangeCustomer = function (e) {
	        this.props.store.changedCustomer(e.target.value);
	    };
	    SelectCustomer.prototype.render = function () {
	        var options = [];
	        this.props.store.commonStore.customers.map(function (customer) {
	            return (options.push(React.createElement("option", {key: customer.id, value: customer.id}, " ", customer.name, " ")));
	        });
	        return (React.createElement("select", {id: "optCustomer", onChange: this.onChangeCustomer.bind(this), className: "form-control select2"}, options));
	    };
	    SelectCustomer = __decorate([
	        mobx_react_1.observer
	    ], SelectCustomer);
	    return SelectCustomer;
	}(React.Component));
	Object.defineProperty(exports, "__esModule", { value: true });
	exports.default = SelectCustomer;
	//# sourceMappingURL=SelectCustomer.js.map

/***/ },

/***/ 208:
/*!*******************************************************************************!*\
  !*** ./wwwroot/libs/tsxbuild/Shared/Stores/Quotations/SalesQuotationStore.js ***!
  \*******************************************************************************/
/***/ function(module, exports, __webpack_require__) {

	"use strict";
	var mobx_1 = __webpack_require__(/*! mobx */ 169);
	var axios = __webpack_require__(/*! axios */ 174);
	var Config = __webpack_require__(/*! Config */ 193);
	var SalesQuotation_1 = __webpack_require__(/*! ./SalesQuotation */ 209);
	var SalesQuotationLine_1 = __webpack_require__(/*! ./SalesQuotationLine */ 210);
	var CommonStore_1 = __webpack_require__(/*! ../Common/CommonStore */ 196);
	var baseUrl = location.protocol
	    + "//" + location.hostname
	    + (location.port && ":" + location.port)
	    + "/";
	var SalesQuotationStore = (function () {
	    function SalesQuotationStore() {
	        this.salesQuotation = new SalesQuotation_1.default();
	        mobx_1.extendObservable(this.salesQuotation, {
	            customerId: this.salesQuotation.customerId,
	            quotationDate: this.salesQuotation.quotationDate,
	            paymentTermId: this.salesQuotation.paymentTermId,
	            referenceNo: this.salesQuotation.referenceNo,
	            salesQuotationLines: []
	        });
	        this.commonStore = new CommonStore_1.default();
	    }
	    SalesQuotationStore.prototype.saveNewQuotation = function () {
	        console.log(JSON.stringify(this.salesQuotation));
	        axios.post(Config.apiUrl + "api/sales/addquotation", JSON.stringify(this.salesQuotation), {
	            headers: {
	                'Content-type': 'application/json'
	            }
	        })
	            .then(function (response) {
	            console.log(response);
	        })
	            .catch(function (error) {
	            console.log(error);
	        });
	    };
	    SalesQuotationStore.prototype.changedCustomer = function (custId) {
	        this.salesQuotation.customerId = custId;
	    };
	    SalesQuotationStore.prototype.changedPaymentTerm = function (termId) {
	        this.salesQuotation.paymentTermId = termId;
	    };
	    SalesQuotationStore.prototype.changedQuotationDate = function (date) {
	        this.salesQuotation.quotationDate = date;
	    };
	    SalesQuotationStore.prototype.addLineItem = function (itemId, measurementId, quantity, amount, discount) {
	        var newLineItem = new SalesQuotationLine_1.default(itemId, measurementId, quantity, amount, discount);
	        this.salesQuotation.salesQuotationLines.push(mobx_1.extendObservable(newLineItem, newLineItem));
	    };
	    SalesQuotationStore.prototype.removeLineItem = function (row) {
	        this.salesQuotation.salesQuotationLines.splice(row, 1);
	    };
	    SalesQuotationStore.prototype.updateLineItem = function (row, targetProperty, value) {
	        if (this.salesQuotation.salesQuotationLines.length > 0)
	            this.salesQuotation.salesQuotationLines[row][targetProperty] = value;
	    };
	    SalesQuotationStore.prototype.grandTotal = function () {
	        var sum = 0;
	        for (var i = 0; i < this.salesQuotation.salesQuotationLines.length; i++) {
	            var lineSum = this.salesQuotation.salesQuotationLines[i].quantity * this.salesQuotation.salesQuotationLines[i].amount;
	            sum = sum + lineSum;
	        }
	        return sum;
	    };
	    SalesQuotationStore.prototype.lineTotal = function (row) {
	        var lineSum = this.salesQuotation.salesQuotationLines[row].quantity * this.salesQuotation.salesQuotationLines[row].amount;
	        ;
	        return lineSum;
	    };
	    return SalesQuotationStore;
	}());
	Object.defineProperty(exports, "__esModule", { value: true });
	exports.default = SalesQuotationStore;
	//# sourceMappingURL=SalesQuotationStore.js.map

/***/ },

/***/ 209:
/*!**************************************************************************!*\
  !*** ./wwwroot/libs/tsxbuild/Shared/Stores/Quotations/SalesQuotation.js ***!
  \**************************************************************************/
/***/ function(module, exports) {

	"use strict";
	var SalesQuotation = (function () {
	    function SalesQuotation() {
	        this.salesQuotationLines = [];
	    }
	    return SalesQuotation;
	}());
	Object.defineProperty(exports, "__esModule", { value: true });
	exports.default = SalesQuotation;
	//# sourceMappingURL=SalesQuotation.js.map

/***/ },

/***/ 210:
/*!******************************************************************************!*\
  !*** ./wwwroot/libs/tsxbuild/Shared/Stores/Quotations/SalesQuotationLine.js ***!
  \******************************************************************************/
/***/ function(module, exports) {

	"use strict";
	var SalesQuotationLine = (function () {
	    function SalesQuotationLine(itemId, measurementId, quantity, amount, discount) {
	        this.itemId = itemId;
	        this.measurementId = measurementId;
	        this.quantity = quantity;
	        this.amount = amount;
	        this.discount = discount;
	    }
	    return SalesQuotationLine;
	}());
	Object.defineProperty(exports, "__esModule", { value: true });
	exports.default = SalesQuotationLine;
	//# sourceMappingURL=SalesQuotationLine.js.map

/***/ }

});
//# sourceMappingURL=addsalesquotation.chunk.js.map