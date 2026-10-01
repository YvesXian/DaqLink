/* 
 * File:   packet.h
 * Author: user
 *
 * Created on 2026年9月29日, 下午 4:52
 */

#ifndef PACKET_H
#define	PACKET_H

#include <stdint.h>

#ifdef	__cplusplus
extern "C" {
#endif

#define PKT_TYPE_DATA        0x01
#define PKT_TYPE_START       0x10
#define PKT_TYPE_STOP        0x11
#define PKT_TYPE_SET_RATE    0x12
#define PKT_TYPE_SET_WAVE    0x13
#define PKT_TYPE_GET_STATUS  0x14
#define PKT_TYPE_ACK         0x80
#define PKT_TYPE_NAK         0x81
#define PKT_TYPE_STATUS      0x82

#define PKT_MAX_PAYLOAD      16         /* 目前最大為 DATA 的 14 byte */
#define PKT_OVERHEAD         6          /* SOF×2 + LEN + TYPE + CRC×2 */
    
    typedef struct
    {
        uint8_t type;
        uint8_t len;
        uint8_t payload[PKT_MAX_PAYLOAD];
    } packet_t;

int      packet_send(uint8_t type, const uint8_t *payload, uint8_t len);
void     packet_tx_pump(void);
uint16_t packet_tx_drop(void);
int packet_rx_feed(uint8_t b, packet_t *pkt);           /* 收到完整且 CRC 正確的封包回傳 1 */
uint16_t packet_rx_crc_err(void);
void put_u16_le(uint8_t *p, uint16_t v);                /* 從 daq.c 搬過來，給 cmd.c 共用 */
uint16_t get_u16_le(const uint8_t *p);

#ifdef	__cplusplus
}
#endif

#endif	/* PACKET_H */

