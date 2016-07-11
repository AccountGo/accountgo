import * as React from "react";
import {observer} from "mobx-react";

@observer
export default class SelectPaymentTerm extends React.Component<any, {}>{
    onChangePaymentTerm(e) {
        this.props.store.changedPaymentTerm(e.target.value);
    }

    render() {
        var options = [];
        this.props.store.commonStore.paymentTerms.map(function (term) {
            return (
                options.push(<option key={ term.id } value={ term.id } > { term.description } </option>)
            );
        });
        // TODO: replace with real values;
        //options.push(<option key="1" value="1"> Payment Term #1 </option>);
        //options.push(<option key="2" value="2"> Payment Term #2 </option>);
        //options.push(<option key="3" value="3"> Payment Term #3 </option>);
        return (
            <select id="optPaymentTerm" onChange={this.onChangePaymentTerm.bind(this) }>
                {options}
            </select>
        );
    }
}