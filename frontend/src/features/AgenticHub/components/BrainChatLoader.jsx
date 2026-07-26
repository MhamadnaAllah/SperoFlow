"use client";

import dynamic from "next/dynamic";

const BrainChat = dynamic(() => import("./BrainChat"), { ssr: false });

export default function BrainChatLoader(props) {
  return <BrainChat {...props} />;
}
