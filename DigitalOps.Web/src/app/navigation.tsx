import {
  AuditOutlined,
  FileDoneOutlined,
  FileSearchOutlined,
  FileTextOutlined,
  FolderOpenOutlined,
  ImportOutlined,
  SearchOutlined,
  SnippetsOutlined,
  TeamOutlined,
  UserOutlined,
} from "@ant-design/icons";
import type { MenuProps } from "antd";
import type { Role } from "../shared/auth/types";

interface NavigationEntry {
  key: string;
  label: string;
  icon: React.ReactNode;
  roles?: readonly Role[];
  section: "common" | "data" | "operations";
}

const navigationEntries: readonly NavigationEntry[] = [
  {
    key: "/incoming-documents",
    label: "Văn bản đến",
    icon: <FolderOpenOutlined />,
    section: "common",
  },
  {
    key: "/outgoing-documents",
    label: "Văn bản đi",
    icon: <FileTextOutlined />,
    section: "common",
  },
  {
    key: "/search",
    label: "Tìm kiếm toàn văn",
    icon: <SearchOutlined />,
    section: "common",
  },
  {
    key: "/staff",
    label: "Staff",
    icon: <TeamOutlined />,
    roles: ["Administrator"],
    section: "data",
  },
  {
    key: "/members",
    label: "Hội viên",
    icon: <UserOutlined />,
    roles: ["Administrator", "Clerk"],
    section: "data",
  },
  {
    key: "/members/import",
    label: "Import hội viên",
    icon: <ImportOutlined />,
    roles: ["Administrator", "Clerk"],
    section: "data",
  },
  {
    key: "/document-types",
    label: "Loại văn bản",
    icon: <SnippetsOutlined />,
    roles: ["Administrator"],
    section: "data",
  },
  {
    key: "/document-templates",
    label: "Mẫu văn bản",
    icon: <FileDoneOutlined />,
    roles: ["Administrator"],
    section: "data",
  },
  {
    key: "/approval-queue",
    label: "Hàng chờ duyệt",
    icon: <AuditOutlined />,
    roles: ["Leader"],
    section: "operations",
  },
  {
    key: "/archive-queue",
    label: "Phát hành / lưu trữ",
    icon: <FileSearchOutlined />,
    roles: ["Clerk"],
    section: "operations",
  },
];

export function getNavigationItems(roles: readonly Role[]): MenuProps["items"] {
  const visibleEntries = navigationEntries.filter(
    (entry) =>
      entry.roles === undefined ||
      entry.roles.some((requiredRole) => roles.includes(requiredRole)),
  );

  return [
    createGroup("Tra cứu chung", "common", visibleEntries),
    createGroup("Quản trị dữ liệu", "data", visibleEntries),
    createGroup("Vận hành", "operations", visibleEntries),
  ].filter((item) => item !== null);
}

export function getSelectedNavigationKey(pathname: string): string | undefined {
  return navigationEntries
    .filter(
      (entry) =>
        pathname === entry.key || pathname.startsWith(`${entry.key}/`),
    )
    .sort((left, right) => right.key.length - left.key.length)[0]?.key;
}

function createGroup(
  label: string,
  section: NavigationEntry["section"],
  visibleEntries: readonly NavigationEntry[],
): NonNullable<MenuProps["items"]>[number] | null {
  const children = visibleEntries
    .filter((entry) => entry.section === section)
    .map(({ key, label: itemLabel, icon }) => ({
      key,
      label: itemLabel,
      icon,
    }));

  return children.length === 0
    ? null
    : {
        type: "group",
        label,
        children,
      };
}
