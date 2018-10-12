
https://adminlte.io/docs/2.4/installation

## Install admin lte via git
- Fork the repository (guide).
- Clone to your machine. 
- git clone https://github.com/YOUR_USERNAME/AdminLTE.git

## Overwrite/Copy these files:

- dist/skins -> wwwroot/adminLTE/css/skins
- dist/AdminLTE.css -> wwwroot/adminLTE/css/AdminLTE.css
- dist/AdminLTE.css -> wwwroot/adminLTE/css/AdminLTE.min.css
- dist/img -> wwwroot/adminLTE/css/img
- dist/js/pages -> wwwroot/adminLTE/css/js/pages
- dist/js/demo.js -> wwwroot/adminLTE/css/js/demo.js
- dist/js/adminlte.js -> wwwroot/adminLTE/css/js/app.js
- dist/js/adminlte.min.js -> wwwroot/adminLTE/css/js/app.min.js

## In adminlte source, open the starter.html. Select all and copy (ctrl+c).
Open accountgo _Layout.cshtml. paste to overwrite.
Change one by one
- <title>Starter</title> -> <title>AcountGo</title>
- <link rel="stylesheet" href="bower_components/bootstrap/dist/css/bootstrap.min.css"> -> <link rel="stylesheet" href="~/plugins/bootstrap/dist/css/bootstrap.min.css">
- <link rel="stylesheet" href="bower_components/font-awesome/css/font-awesome.min.css"> -> <link rel="stylesheet" href="https://cdnjs.cloudflare.com/ajax/libs/font-awesome/4.7.0/css/font-awesome.min.css">
- <link rel="stylesheet" href="bower_components/Ionicons/css/ionicons.min.css"> -> <link rel="stylesheet" href="https://cdnjs.cloudflare.com/ajax/libs/ionicons/4.4.1/css/ionicons.min.css">
- <link rel="stylesheet" href="dist/css/AdminLTE.min.css"> -> <link rel="stylesheet" href="~/adminLTE/css/AdminLTE.min.css">
- <link rel="stylesheet" href="dist/css/skins/skin-blue.min.css"> -> 
- add <link rel="stylesheet" href="~/adminLTE/css/AdminLTEOverride.css">

## Then at the bottom before </head> tag, add these lines
    <script src="~/scripts/vendor.chunk.js"></script>
    <script src="https://cdnjs.cloudflare.com/ajax/libs/bluebird/3.5.2/bluebird.min.js"></script>

## Change logo

From:
    <!-- Logo -->
    <a href="index2.html" class="logo">
      <!-- mini logo for sidebar mini 50x50 pixels -->
      <span class="logo-mini"><b>A</b>LT</span>
      <!-- logo for regular state and mobile devices -->
      <span class="logo-lg"><b>Admin</b>LTE</span>
    </a>
To
    <!-- Logo -->
    <a href="~/" class="logo">
      <!-- mini logo for sidebar mini 50x50 pixels -->
      <span class="logo-mini"><b>AG</span>
      <!-- logo for regular state and mobile devices -->
      <span class="logo-lg"><b>AccountGo</span>
    </a>

## Change user image
From:
                        <!-- User Image -->
                        <img src="dist/img/user2-160x160.jpg" class="img-circle" alt="User Image">
To:
<img src="~/adminLTE/img/user2-160x160.png" class="img-circle" alt="User Image">

## Change user image in the navbar,
From:
            <!-- Menu Toggle Button -->
            <a href="#" class="dropdown-toggle" data-toggle="dropdown">
              <!-- The user image in the navbar-->
              <img src="dist/img/user2-160x160.jpg" class="user-image" alt="User Image">
              <!-- hidden-xs hides the username on small devices so only the image appears. -->
              <span class="hidden-xs">Alexander Pierce</span>
            </a>
To:
                            <!-- Menu Toggle Button -->
                            <a href="#" class="dropdown-toggle" data-toggle="dropdown">
                                <!-- The user image in the navbar-->
                                <img src="~/adminLTE/img/user2-160x160.png" class="user-image" alt="User Image">
                                <!-- hidden-xs hides the username on small devices so only the image appears. -->
                                <span class="hidden-xs">@User.Identity.Name</span>
                            </a>
### Next

From:
              <!-- The user image in the menu -->
              <li class="user-header">
                <img src="dist/img/user2-160x160.jpg" class="img-circle" alt="User Image">

                <p>
                  Alexander Pierce - Web Developer
                  <small>Member since Nov. 2012</small>
                </p>
              </li>
To:
                                <!-- The user image in the menu -->
                                <li class="user-header">
                                    <img src="~/adminLTE/img/user2-160x160.png" class="img-circle" alt="User Image" />
                                    <p>
                                        Marvin Perez - Founder
                                        <small>Member since Jan. 2010</small>
                                    </p>
                                </li>

## Replace the side bar menu

From:

      <ul class="sidebar-menu" data-widget="tree">
        <li class="header">HEADER</li>
        <!-- Optionally, you can add icons to the links -->
        <li class="active"><a href="#"><i class="fa fa-link"></i> <span>Link</span></a></li>
        <li><a href="#"><i class="fa fa-link"></i> <span>Another Link</span></a></li>
        <li class="treeview">
          <a href="#"><i class="fa fa-link"></i> <span>Multilevel</span>
            <span class="pull-right-container">
                <i class="fa fa-angle-left pull-right"></i>
              </span>
          </a>
          <ul class="treeview-menu">
            <li><a href="#">Link in level 2</a></li>
            <li><a href="#">Link in level 2</a></li>
          </ul>
        </li>
      </ul>

To:

                <ul class="sidebar-menu" data-widget="tree">
                    <li class="header">NAVIGATION</li>
                    <!-- Optionally, you can add icons to the links -->
                    <li class="active">
                        <!--<a href="#"><i class="fa fa-dashboard"></i> <span>Dashboard</span></a>-->
                        <a href="~/"><i class="fa fa-dashboard"></i><span>Dashboard</span></a>
                    </li>
                    <li class="treeview">
                        <a href="#"><i class="fa fa-industry"></i> <span>Accounts Receivable</span> <i class="fa fa-angle-left pull-right"></i></a>
                        <ul class="treeview-menu">
                            <li>
                                <a href="~/quotations/">Sales Quotations</a>
                                <!--<a href="#">Sales Quotations</a>-->
                            </li>
                            <li>
                                <a href="~/sales/salesorders">Sales Orders</a>
                                <!--<a href="#">Sales Orders</a>-->
                            </li>
                            <li>
                                <!--<a href="#">Sales Receipts</a>-->
                                <a href="~/sales/salesreceipts">Sales Receipts</a>
                            </li>
                            <li>
                                <!--<a href="#">Sales Invoices</a>-->
                                <a href="~/sales/salesinvoices">Sales Invoices</a>
                            </li>
                            <li>
                                <a href="~/sales/customers">Customers</a>
                            </li>
                        </ul>
                    </li>
                    <li class="treeview">
                        <a href="#"><i class="fa fa-link"></i> <span>Accounts Payable</span> <i class="fa fa-angle-left pull-right"></i></a>
                        <ul class="treeview-menu">
                            <li>
                                <!--<a href="#">Purchase Orders</a>-->
                                <a href="~/purchasing/purchaseorders">Purchase Orders</a>
                            </li>
                            <li>
                                <!--<a href="#">Purchase Invoices</a>-->
                                <a href="~/purchasing/purchaseinvoices">Purchase Invoices</a>
                            </li>
                            <li>
                                <a href="~/purchasing/vendors">Vendors</a>
                            </li>
                        </ul>
                    </li>
                    <li class="treeview">
                        <a href="#"><i class="fa fa-wrench"></i> <span>Inventory Control</span> <i class="fa fa-angle-left pull-right"></i></a>
                        <ul class="treeview-menu">
                            <li>
                                <a href="~/inventory/items">Items</a>
                            </li>
                            <li>
                                <!--<a href="#">Inventory Control Journal</a>-->
                                <a href="~/inventory/icj">Inventory Control Journal</a>
                            </li>
                        </ul>
                    </li>
                    <li class="treeview">
                        <a href="#"><i class="fa fa-bank"></i> <span>Financials</span> <i class="fa fa-angle-left pull-right"></i></a>
                        <ul class="treeview-menu">
                            <li>
                                <!--<a href="#">Journal Entries</a>-->
                                <a href="~/financials/journalentries">Journal Entries</a>
                            </li>
                            <li>
                                <!--<a href="#">General Ledgers</a>-->
                                <a href="~/financials/generalledger">General Ledgers</a>
                            </li>
                            <li>
                                <!--<a href="#">Taxes</a>-->
                                <a href="~/tax/taxes">Taxes</a>
                            </li>
                            <li>
                                <!--<a href="#">Taxes</a>-->
                                <a href="~/financials/accounts">Accounts</a>
                            </li>
                            <li>
                                <a href="~/financials/banks">Banks</a>
                            </li>
                        </ul>
                    </li>
                    <li class="treeview">
                        <a href="#"><i class="fa fa-line-chart"></i> <span>Reports</span> <i class="fa fa-angle-left pull-right"></i></a>
                        <ul class="treeview-menu">
                            <li>
                                <!--<a href="#">Balance Sheet</a>-->
                                <a href="~/financials/balancesheet">Balance Sheet</a>
                            </li>
                            <li>
                                <!--<a href="#">Income Statement</a>-->
                                <a href="~/financials/incomestatement">Income Statement</a>
                            </li>
                            <li>
                                <!--<a href="#">Trial Balance</a>-->
                                <a href="~/financials/trialbalance">Trial Balance</a>
                            </li>
                        </ul>
                    </li>
                    <li class="treeview">
                        <a href="#"><i class="fa fa-group"></i> <span>Organization Settings</span> <i class="fa fa-angle-left pull-right"></i></a>
                        <ul class="treeview-menu">
                            <li>
                                <!--<a href="#">Company</a>-->
                                <a href="~/administration/company">Company</a>
                            </li>
                            <li>
                                <!--<a href="#">Setup</a>-->
                                <a href="~/administration/settings">Settings</a>
                            </li>
                        </ul>
                    </li>
                    <li class="treeview">
                        <a href="#"><i class="fa fa-desktop"></i> <span>System Administration</span> <i class="fa fa-angle-left pull-right"></i></a>
                        <ul class="treeview-menu">
                            @if (User.IsInRole("SystemAdministrators"))
                            {
                                <li>
                                    <a href="#">
                                        Security
                                        <span class="pull-right-container">
                                            <i class="fa fa-angle-left pull-right"></i>
                                        </span>
                                    </a>
                                    <ul class="treeview-menu menu-open" style="display: block;">
                                        <li><a href="~/administration/users">Users</a></li>
                                        <li><a href="~/administration/roles">Roles</a></li>
                                        <li><a href="~/administration/groups">Groups</a></li>
                                    </ul>
                                </li>
                                <li>
                                    <a href="~/administration/auditlogs">Audit Logs</a>
                                </li>
                            }
                        </ul>
                    </li>
                </ul>

## Comment out 

      <!-- Sidebar user panel (optional) -->
      <div class="user-panel">
        <div class="pull-left image">
          <img src="dist/img/user2-160x160.jpg" class="img-circle" alt="User Image">
        </div>
        <div class="pull-left info">
          <p>Alexander Pierce</p>
          <!-- Status -->
          <a href="#"><i class="fa fa-circle text-success"></i> Online</a>
        </div>
      </div>

      <!-- search form (Optional) -->
      <form action="#" method="get" class="sidebar-form">
        <div class="input-group">
          <input type="text" name="q" class="form-control" placeholder="Search...">
          <span class="input-group-btn">
              <button type="submit" name="search" id="search-btn" class="btn btn-flat"><i class="fa fa-search"></i>
              </button>
            </span>
        </div>
      </form>
      <!-- /.search form -->

## Replace User Account Menu -> Menu Toggle Button
From:
            <a href="#" class="dropdown-toggle" data-toggle="dropdown">
              <!-- The user image in the navbar-->
              <img src="dist/img/user2-160x160.jpg" class="user-image" alt="User Image">
              <!-- hidden-xs hides the username on small devices so only the image appears. -->
              <span class="hidden-xs">Alexander Pierce</span>
            </a>
To:
                            <a href="#" class="dropdown-toggle" data-toggle="dropdown">
                                <!-- The user image in the navbar-->
                                <img src="~/adminLTE/img/user2-160x160.png" class="user-image" alt="User Image">
                                <!-- hidden-xs hides the username on small devices so only the image appears. -->
                                <span class="hidden-xs">@User.Identity.Name</span>
                            </a>

## Replace the main content
    <!-- Main content -->
    <section class="content container-fluid">

      <!--------------------------
        | Your Page Content Here |
        -------------------------->

    </section>
    <!-- /.content -->

To:
            <!-- Main content -->
            <section class="content">
                <!-- Your Page Content Here -->
                @RenderBody()
                <div class='clearfix'></div>
            </section>
            <!-- /.content -->


## Change the required js scripts
From:
<!-- ./wrapper -->

<!-- REQUIRED JS SCRIPTS -->

<!-- jQuery 3 -->
<script src="bower_components/jquery/dist/jquery.min.js"></script>
<!-- Bootstrap 3.3.7 -->
<script src="bower_components/bootstrap/dist/js/bootstrap.min.js"></script>
<!-- AdminLTE App -->
<script src="dist/js/adminlte.min.js"></script>

To:
    <!-- ./wrapper -->
    <!-- REQUIRED JS SCRIPTS -->
    <!-- popper.js 1.12.9 -->
    <script src="~/plugins/popper.js/dist/umd/popper.js"></script>
    <!-- jQuery 2.2.0 -->
    <script src="~/plugins/jquery/dist/jquery.min.js"></script>
    <!-- Bootstrap 3.3.6 -->
    <script src="~/plugins/bootstrap/dist/js/bootstrap.min.js"></script>
    <!-- AdminLTE App -->
    <script src="~/adminLTE/js/app.js"></script>
    <!--AG-GRID-->
    <script src="~/plugins/ag-grid/dist/ag-grid.js?ignore=notused24"></script>
    <!--TODO : this is temporary solution.-->
    <script>
        $(document).ready(function () {
            $('li').each(function () {
                if (window.location.href.indexOf($(this).find('a:first').attr('href')) > -1) {
                    $(this).addClass('active').siblings().removeClass('active');
                    $(this).parent().parent().addClass('treeview active');
                }
            });
        });
    </script>
    <!--//END TODO-->
    @RenderSection("scripts", required: false)

## Comment out 
        <!-- Content Header (Page header) -->
    <!-- <section class="content-header">
      <h1>
        Page Header
        <small>Optional description</small>
      </h1>
      <ol class="breadcrumb">
        <li><a href="#"><i class="fa fa-dashboard"></i> Level</a></li>
        <li class="active">Here</li>
      </ol>
    </section> -->
    <!-- Main content -->

    docker rmi $(docker images --filter "dangling=true" -q --no-trunc)