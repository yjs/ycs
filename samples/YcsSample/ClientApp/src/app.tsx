import { Route } from 'react-router';
import { Navbar, Nav, NavItem, NavLink } from 'reactstrap';
import { Link } from 'react-router-dom';
import { YjsMonacoEditor } from './components/monacoEditor';

export const App = () => {
  return (
    <div>
      <header>
        <Navbar color="light" light expand="md">
          <Nav navbar>
            <NavItem>
              <NavLink tag={Link} to="/">Monaco Editor</NavLink>
            </NavItem>
          </Nav>
        </Navbar>
      </header>

      <Route exact path='/' component={YjsMonacoEditor} />
    </div>
  );
};
