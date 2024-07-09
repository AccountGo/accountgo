import { useState } from "react"

const Counter = () => {
    const [count, setCount] = useState(1)
    return(
        <>
            <h1>Count is {count}</h1>
            <button onClick={() => setCount(count + 1)}>Increment (+)</button>
            &nbsp;&nbsp;&nbsp;&nbsp;
            <button onClick={() => setCount(count - 1)}>Decrement (-)</button>
        </>
    )
}

export default Counter
