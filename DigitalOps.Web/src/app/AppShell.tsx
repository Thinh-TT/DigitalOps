import {
  BellOutlined,
  DownOutlined,
  KeyOutlined,
  LogoutOutlined,
  UserOutlined,
} from "@ant-design/icons";
import {
  Avatar,
  Button,
  Dropdown,
  Layout,
  Menu,
  Space,
  Typography,
  type MenuProps,
} from "antd";
import { Outlet, useLocation, useNavigate } from "react-router";
import { useAuth } from "../shared/auth/auth-context";
import {
  getNavigationItems,
  getSelectedNavigationKey,
} from "./navigation";

const { Header, Sider, Content } = Layout;

export function AppShell() {
  const { currentUser, logout } = useAuth();
  const location = useLocation();
  const navigate = useNavigate();

  if (currentUser === null) {
    return null;
  }

  const accountMenu: MenuProps = {
    items: [
      {
        key: "change-password",
        label: "Đổi mật khẩu",
        icon: <KeyOutlined />,
      },
      {
        type: "divider",
      },
      {
        key: "logout",
        label: "Đăng xuất",
        icon: <LogoutOutlined />,
        danger: true,
      },
    ],
    onClick: ({ key }) => {
      if (key === "change-password") {
        navigate("/change-password");
      } else if (key === "logout") {
        logout();
        navigate("/login", { replace: true });
      }
    },
  };

  return (
    <Layout className="app-shell">
      <Sider className="app-sidebar" width={240} theme="light">
        <div className="app-brand">
          <Typography.Title level={4}>DigitalOps</Typography.Title>
          <Typography.Text type="secondary">
            Điều hành văn bản
          </Typography.Text>
        </div>
        <Menu
          aria-label="Điều hướng chính"
          mode="inline"
          items={getNavigationItems(currentUser.roles)}
          selectedKeys={
            getSelectedNavigationKey(location.pathname)
              ? [getSelectedNavigationKey(location.pathname)!]
              : []
          }
          onClick={({ key }) => navigate(key)}
        />
      </Sider>

      <Layout>
        <Header className="app-header">
          <Typography.Text strong>Hệ thống điều hành số</Typography.Text>
          <Space size="middle">
            <Button
              aria-label="Mở thông báo"
              type="text"
              icon={<BellOutlined />}
              onClick={() => navigate("/reminders")}
            />
            <Dropdown menu={accountMenu} trigger={["click"]}>
              <Button type="text">
                <Space>
                  <Avatar size="small" icon={<UserOutlined />} />
                  <span>{currentUser.staff.fullName}</span>
                  <DownOutlined />
                </Space>
              </Button>
            </Dropdown>
          </Space>
        </Header>

        <Content className="app-content">
          <Outlet />
        </Content>
      </Layout>
    </Layout>
  );
}
