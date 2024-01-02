// import Heading from "./components/Heading"
// import Section from "./components/Section"
// import Counter from "./components/Counter"
// import List from "./components/List"
// import Home from "./components/Home/Index"
import { Outlet } from "react-router"
import Home from "./components/Home/Index"

// https://www.youtube.com/watch?v=G7UzhrNX60o

function App() {

  return (
    <>
      <Home />
      <Outlet />
    </>
  )
}

export default App
