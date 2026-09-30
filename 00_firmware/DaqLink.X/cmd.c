#include "cmd.h"
#include "packet.h"
#include "uart1.h"
#include "daq.h"

#define ERR_UNKNOWN_CMD          0x01
#define ERR_BAD_LEN              0x02
#define ERR_OUT_OF_RANGE         0x03
#define ERR_BUSY                 0x04

#define RATE_MIN                 10
#define RATE_MAX                 200

static void reply(uint8_t type, const uint8_t *p, uint8_t len)
{
    uint32_t saved = daq_lock();
    packet_send(type, p, len);
    daq_unlock(saved);
}

static void ack(uint8_t cmd)
{
    reply(PKT_TYPE_ACK, &cmd, 1);
}

static void nak(uint8_t cmd, uint8_t err)
{
    uint8_t p[2];
    
    p[0] = cmd;
    p[1] = err;
    reply(PKT_TYPE_NAK, p, 2);
}

static void send_status(void)
{
    uint8_t p[8];
    
    p[0] = (uint8_t)daq_is_running();
    p[1] = 0;
    put_u16_le(&p[2], daq_get_rate());
    put_u16_le(&p[4], packet_rx_crc_err());
    put_u16_le(&p[6], packet_tx_drop());
    reply(PKT_TYPE_STATUS, p, 8);
}

static void dispatch(const packet_t *pkt)
{
    uint16_t hz;

    switch (pkt->type)
    {
    case PKT_TYPE_START:
        if (pkt->len != 0) { nak(pkt->type, ERR_BAD_LEN); break; }
        daq_start();
        ack(pkt->type);
        break;

    case PKT_TYPE_STOP:
        if (pkt->len != 0) { nak(pkt->type, ERR_BAD_LEN); break; }
        daq_stop();
        ack(pkt->type);
        break;

    case PKT_TYPE_SET_RATE:
        if (pkt->len != 2)    { nak(pkt->type, ERR_BAD_LEN); break; }
        if (daq_is_running()) { nak(pkt->type, ERR_BUSY);    break; }
        hz = (uint16_t)(pkt->payload[0] | (pkt->payload[1] << 8));
        if (hz < RATE_MIN || hz > RATE_MAX) { nak(pkt->type, ERR_OUT_OF_RANGE); break; }
        daq_set_rate(hz);
        ack(pkt->type);
        break;

    case PKT_TYPE_GET_STATUS:
        if (pkt->len != 0) { nak(pkt->type, ERR_BAD_LEN); break; }
        send_status();                  /* 協定 5.5:只回 STATUS,不另外回 ACK */
        break;

    default:                            /* 含 SET_WAVE,P-5 再實作 */
        nak(pkt->type, ERR_UNKNOWN_CMD);
        break;
    }
}

void cmd_poll(void)
{
    static packet_t pkt;
    int c;

    while ((c = uart1_getc()) >= 0)
        if (packet_rx_feed((uint8_t)c, &pkt))
            dispatch(&pkt);
}
